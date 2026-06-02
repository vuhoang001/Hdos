# 05 — Nginx Gateway & HTTPS/TLS

nginx là điểm vào duy nhất của hệ thống. Tất cả request từ client đều đi qua đây trước khi đến microservice.

**Vai trò hiện tại (kể từ refactor 2026-05-20):**

- TLS termination (`8443`) + redirect `8080 → 8443`
- Reverse proxy theo prefix path (`/auth`, `/orders`, `/notifications`, `/m01`, `/async`)
- CORS authority (whitelist origin, strip CORS từ upstream, xử lý preflight)
- WebSocket / SSE upgrade

**Không còn làm:**

- ~~Verify JWT (`auth_request`)~~ — services tự verify
- ~~Bơm headers `X-User-Id/Email/Roles/Permissions`~~ — permissions đã nằm trong JWT
- ~~Trả error JSON `@unauthorized` / `@forbidden`~~ — services trả 401/403 chuẩn ASP.NET

Lý do bỏ: xem [06 — Xác thực](./06-xac-thuc.md#1-tổng-quan-luồng).

---

## Tại sao nginx thay vì C# YARP?

| | nginx | YARP (C#) |
|--|-------|-----------|
| Cấu hình | File text, không build | Code C#, cần build + deploy |
| Reload | `nginx -s reload` (không downtime) | Phải restart service |
| Memory | ~5MB | ~50-100MB |
| CORS | Vài dòng config | Viết middleware |
| Battle-tested | 20+ năm production | Microsoft, còn mới |
| Custom logic | Giới hạn (Lua) | C# thoải mái |

**Kết luận:** với use case hiện tại (routing + CORS + TLS), nginx đủ dùng và đơn giản hơn nhiều. Auth giờ là việc của services, nginx không phải lo.

---

## Cấu trúc config

File `nginx/nginx.conf` chia 4 block chính:

1. **Maps**: `$connection_upgrade` (WebSocket), `$cors_origin` (CORS whitelist)
2. **Upstreams**: 5 services + frontend host
3. **HTTP → HTTPS server (8080)**: redirect 301 sang HTTPS
4. **HTTPS server (8443)**: routing thật, TLS, CORS, proxy

### Routing pattern (cho mỗi service)

```nginx
# Swagger UI — anonymous, prefix-match thắng regex bên dưới
location ^~ /orders/swagger {
    proxy_pass http://orderservice;
}

# Business routes — service tự enforce auth/permission
location ~ ^/orders($|/) {
    if ($request_method = OPTIONS) { return 418; }   # → @cors_preflight
    proxy_pass http://orderservice;
}
```

Mọi `proxy_set_header` chung (Host, X-Real-IP, X-Forwarded-*, Upgrade) được khai báo 1 lần ở server level — kế thừa xuống mọi `location` không override.

### CORS

`$cors_origin` map cho phép `localhost`, `192.168.x.x` (LAN). Production thêm domain thật. Preflight `OPTIONS` được "redirect" sang `@cors_preflight` qua `error_page 418` để tránh duplicate config.

```nginx
map $http_origin $cors_origin {
    "~^https?://localhost(:\d+)?$"           $http_origin;
    "~^https?://192\.168\.\d+\.\d+(:\d+)?$" $http_origin;
    default                                  "";
}
```

`proxy_hide_header` strip mọi `Access-Control-*` từ upstream để nginx là CORS authority duy nhất.

---

## Auth flow đã đơn giản hoá

```
Client: GET /m01/dashboard/summary
        Authorization: Bearer eyJhbGci...
         │
         ▼
      nginx (location ~ ^/m01($|/))
         │  • Strip upstream CORS, set X-Forwarded-*
         │  • KHÔNG call AuthService
         ▼
      m01service:8080/m01/dashboard/summary
         │  • JwtBearer verify HS256 + iss/aud/exp
         │  • Đọc claim "permission" từ JWT
         │  • [Authorize(Policy = HdosPermissions.M01Read)] enforce
         ▼
      200 / 401 / 403  → nginx → Client
```

> Permission nằm thẳng trong JWT, không cần header `X-User-Permissions`. Chi tiết: [06 — Xác thực](./06-xac-thuc.md).

---

## Endpoint đặc biệt

| Path | Xử lý |
|------|-------|
| `/health` | nginx trả 200 JSON `{"status":"OK","service":"nginx-gateway"}` |
| `/notifications/sse` | Bypass `proxy_buffering`, `proxy_read_timeout 24h` cho EventSource. Token qua `?access_token=` query param. |
| `/auth/*` | Forward tới authservice (login/register là anonymous, các endpoint admin được service tự gác bằng `[Authorize(Roles="admin")]`). |
| `/` (catch-all) | Forward tới `host.docker.internal:4000` (Next.js frontend). |

---

## Swagger với prefix routing

Mỗi service cấu hình Swagger `RoutePrefix` khớp nginx prefix qua helper chung `UseHdosSwaggerUi(servicePrefix, title)`:

```csharp
// Program.cs của mỗi service
app.UseHdosSwaggerUi("orders", "OrderService v1");
// → JSON tại /orders/swagger/v1/swagger.json
// → UI   tại /orders/swagger/index.html
```

Nginx có rule `location ^~ /orders/swagger { proxy_pass http://orderservice; }` — `^~` đảm bảo prefix match thắng các regex `^/orders(...)` (anonymous, không cần token).

---

## Deploy nginx config mới

nginx dùng volume mount — không cần rebuild image:

```yaml
# docker-compose.yml
nginx:
  image: nginx:1.27-alpine
  volumes:
    - ./nginx/nginx.conf:/etc/nginx/nginx.conf:ro
```

Quy trình update:

```bash
# 1. Sửa nginx/nginx.conf
# 2. Kiểm tra syntax
docker exec hdos-nginx nginx -t

# 3. Reload không downtime
docker exec hdos-nginx nginx -s reload

# Hoặc nếu cần đổi config lớn (thêm upstream mới...):
docker restart hdos-nginx
```

**Lưu ý:** `nginx -s reload` reload config mà không đóng existing connections. `docker restart` có brief downtime (~1 giây).

---

## Thêm service mới

1. Thêm upstream trong `nginx.conf`:

```nginx
upstream newservice { server newservice:8080; }
```

2. Thêm routes (copy pattern hiện có):

```nginx
location ^~ /new/swagger {
    proxy_pass http://newservice;
}

location ~ ^/new($|/) {
    if ($request_method = OPTIONS) { return 418; }
    proxy_pass http://newservice;
}
```

3. Reload: `docker exec hdos-nginx nginx -s reload`.
4. Service tự enforce auth bằng `[Authorize(Policy = HdosPermissions.X)]` trên controller.

---

## Internal monitoring server

Server riêng listen `8081` chỉ trong Docker network (không expose ra host) phục vụ Prometheus scrape:

```nginx
server {
    listen 8081;
    location /nginx_status {
        stub_status;
        allow 172.16.0.0/12;
        allow 192.168.0.0/16;
        allow 10.0.0.0/8;
        deny all;
    }
}
```

Prometheus exporter (`nginx-prometheus-exporter`) gọi vào endpoint này — chi tiết: [08 — Quan sát hệ thống](./08-quan-sat-he-thong.md).

---

## HTTPS & TLS

Tất cả traffic vào hệ thống đi qua nginx HTTPS tại cổng `8443` (self-signed cert cho dev). Internal Docker network chạy HTTP plain.

### Vì sao HTTPS

- Browser hiện đại block fetch HTTP từ trang HTTPS (Mixed Content).
- `localStorage` của site HTTPS bị isolate khỏi site HTTP → không share token được giữa hai origin.
- Khi xử lý xác thực, mọi trao đổi token phải qua kênh mã hoá.

Local dev `https://localhost:8443` đủ — chấp nhận self-signed cert một lần.

### Kiến trúc TLS

```
Browser
  │  HTTPS (TLS 1.2/1.3, self-signed dev cert)
  ▼
nginx :8443 ──┬─ /auth/*           → authservice:8080  (HTTP nội bộ)
              ├─ /orders/*         → orderservice:8080
              ├─ /notifications/*  → notificationservice:8080
              ├─ /m01/*            → m01service:8080
              ├─ /async/*          → asyncgateway:8080
              └─ /                 → frontend:4000

nginx :8080 → 301 → https://$host:8443  (HTTP → HTTPS redirect)
```

Tất cả service backend chạy HTTP `:8080` trong Docker network; nginx chỉ termination TLS ở edge.

### Self-signed cert (dev)

Cert được tạo tự động lần đầu nginx khởi động qua `nginx/entrypoint.sh` (volume `hdos-nginx-ssl`).

```bash
# Xem cert đang dùng
docker exec hdos-nginx openssl x509 -in /etc/nginx/ssl/hdos.crt -noout -subject -dates

# Force regenerate (xoá volume)
docker compose down
docker volume rm hdos_hdos-nginx-ssl
docker compose up -d
```

CN của cert mặc định là `localhost`. Để cert valid cho IP server cụ thể, sửa `nginx/entrypoint.sh` (SAN field) trước khi build.

### Production: thay cert thật

Mount cert riêng vào `/etc/nginx/ssl/`:

```yaml
# docker-compose.server.yml (override)
nginx:
  volumes:
    - /opt/certs/hdos.crt:/etc/nginx/ssl/hdos.crt:ro
    - /opt/certs/hdos.key:/etc/nginx/ssl/hdos.key:ro
```

Hoặc dùng Let's Encrypt + certbot:

```bash
sudo certbot --nginx -d api.hdos.example.com
```

### Cấu hình nginx TLS (`nginx/nginx.conf`)

```nginx
server {
    listen 8443 ssl;
    ssl_certificate     /etc/nginx/ssl/hdos.crt;
    ssl_certificate_key /etc/nginx/ssl/hdos.key;
    ssl_protocols       TLSv1.2 TLSv1.3;
    ssl_ciphers         HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers on;
    ssl_session_cache   shared:SSL:10m;
    ssl_session_timeout 10m;
    # ... locations ...
}
```

### Browser cảnh báo "Not Secure" (dev)

Self-signed cert sẽ làm Chrome/Firefox báo `NET::ERR_CERT_AUTHORITY_INVALID`. Một lần click **Advanced → Proceed** là xong (browser cache cho origin đó).

Để khử cảnh báo hẳn trong dev:

1. Copy cert ra: `docker cp hdos-nginx:/etc/nginx/ssl/hdos.crt /tmp/hdos.crt`
2. Trust cert ở OS:
   - Linux: `sudo cp /tmp/hdos.crt /usr/local/share/ca-certificates/ && sudo update-ca-certificates`
   - macOS: `sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain /tmp/hdos.crt`
   - Windows: import vào "Trusted Root Certification Authorities".

### Troubleshooting TLS

| Triệu chứng | Nguyên nhân | Fix |
|-------------|-------------|-----|
| `ERR_CERT_AUTHORITY_INVALID` | Self-signed cert, chưa accept | Click Advanced → Proceed (dev). Prod: dùng cert thật |
| nginx restart loop, log "SSL: error:0B080074" | Cert/key không match | Xoá volume nginx-ssl, để entrypoint regen |
| FE gọi API trả `502 Bad Gateway` | Service backend chưa up | `docker compose ps`, đợi healthcheck pass |
| HTTPS hoạt động nhưng `/auth/login` 503 | AuthService chưa migrate DB | Xem log `docker logs hdos-authservice` |
