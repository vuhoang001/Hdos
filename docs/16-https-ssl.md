# 16 — HTTPS & TLS

Tất cả traffic vào hệ thống đi qua nginx HTTPS tại cổng `8443` (self-signed cert cho dev). Internal Docker network chạy HTTP plain.

---

## 1. Vì sao HTTPS

- Browser hiện đại block fetch HTTP từ trang HTTPS (Mixed Content).
- `localStorage` của site HTTPS bị isolate khỏi site HTTP → không share token được giữa hai origin.
- Khi xử lý xác thực, mọi trao đổi token phải qua kênh mã hoá.

Local dev `https://localhost:8443` đủ — chấp nhận self-signed cert một lần.

---

## 2. Kiến trúc

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

---

## 3. Self-signed cert (dev)

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

---

## 4. Production: thay cert thật

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

---

## 5. Cấu hình nginx TLS (`nginx/nginx.conf`)

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

---

## 6. Browser cảnh báo "Not Secure" (dev)

Self-signed cert sẽ làm Chrome/Firefox báo `NET::ERR_CERT_AUTHORITY_INVALID`. Một lần click **Advanced → Proceed** là xong (browser cache cho origin đó).

Để khử cảnh báo hẳn trong dev:

1. Copy cert ra: `docker cp hdos-nginx:/etc/nginx/ssl/hdos.crt /tmp/hdos.crt`
2. Trust cert ở OS:
   - Linux: `sudo cp /tmp/hdos.crt /usr/local/share/ca-certificates/ && sudo update-ca-certificates`
   - macOS: `sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain /tmp/hdos.crt`
   - Windows: import vào "Trusted Root Certification Authorities".

---

## 7. Troubleshooting

| Triệu chứng | Nguyên nhân | Fix |
|-------------|-------------|-----|
| `ERR_CERT_AUTHORITY_INVALID` | Self-signed cert, chưa accept | Click Advanced → Proceed (dev). Prod: dùng cert thật |
| nginx restart loop, log "SSL: error:0B080074" | Cert/key không match | Xoá volume nginx-ssl, để entrypoint regen |
| FE gọi API trả `502 Bad Gateway` | Service backend chưa up | `docker compose ps`, đợi healthcheck pass |
| HTTPS hoạt động nhưng `/auth/login` 503 | AuthService chưa migrate DB | Xem log `docker logs hdos-authservice` |

---

## 8. Cross-reference

- [05 — Nginx Gateway](./05-nginx-gateway.md) — chi tiết location blocks (nginx giờ chỉ reverse proxy).
- [06 — Xác thực & Phân quyền](./06-xac-thuc.md) — JWT verify ở services.
- [11 — Local Dev & Deploy](./11-local-dev-va-deploy.md) — chạy stack lên local.
