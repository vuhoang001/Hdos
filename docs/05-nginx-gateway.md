# 05 — Nginx Gateway

nginx là điểm vào duy nhất của hệ thống. Tất cả request từ client đều đi qua đây trước khi đến microservice.

---

## Tại sao nginx thay vì C# YARP?

| | nginx | YARP (C#) |
|--|-------|-----------|
| Cấu hình | File text, không build | Code C#, cần build + deploy |
| Reload | `nginx -s reload` (không downtime) | Phải restart service |
| Memory | ~5MB | ~50-100MB |
| CORS | 5 dòng config | Viết middleware |
| Battle-tested | 20+ năm production | Microsoft, còn mới |
| Custom logic | Giới hạn (Lua) | C# thoải mái |

**Kết luận:** Với use case hiện tại (routing, CORS, JWT validation), nginx đủ dùng và đơn giản hơn nhiều.

---

## Config đầy đủ annotated

```nginx
events {
    worker_connections 1024;    # Tối đa 1024 concurrent connections
}

http {
    # ── WebSocket upgrade mapping ─────────────────────────────────────────
    # SignalR dùng WebSocket. Khi client gửi "Upgrade: websocket",
    # nginx cần biết forward Connection header là gì.
    map $http_upgrade $connection_upgrade {
        default upgrade;    # Có Upgrade header → forward "upgrade"
        ''      close;      # Không có → forward "close" (HTTP thường)
    }

    # ── Upstream definitions ──────────────────────────────────────────────
    # Tên DNS resolve trong Docker network hdos-net
    upstream authservice         { server authservice:8080; }
    upstream orderservice        { server orderservice:8080; }
    upstream notificationservice { server notificationservice:8080; }
    upstream m01service          { server m01service:8080; }

    server {
        listen 8080;    # Container port (host map 5000→8080)

        # ── Strip upstream CORS headers ───────────────────────────────────
        # Các service bên trong cũng có CORS middleware (UseHdosCors).
        # Nếu không strip, browser nhận 2 "Access-Control-Allow-Origin" → lỗi CORS.
        proxy_hide_header Access-Control-Allow-Origin;
        proxy_hide_header Access-Control-Allow-Methods;
        proxy_hide_header Access-Control-Allow-Headers;
        proxy_hide_header Access-Control-Allow-Credentials;
        proxy_hide_header Access-Control-Max-Age;

        # ── nginx thêm CORS headers của mình ─────────────────────────────
        # always = thêm vào cả 4xx/5xx response (quan trọng cho 401/403)
        add_header Access-Control-Allow-Origin  * always;
        add_header Access-Control-Allow-Methods "GET, POST, PUT, DELETE, PATCH, OPTIONS" always;
        add_header Access-Control-Allow-Headers "*" always;   # * = cho phép mọi header
        add_header Access-Control-Max-Age       3600 always;

        # ── Preflight OPTIONS ─────────────────────────────────────────────
        # Browser gửi OPTIONS trước mỗi cross-origin request (CORS preflight).
        # Xử lý ngay tại đây, không cần forward xuống service.
        if ($request_method = OPTIONS) {
            return 204;    # No Content
        }

        # ── Gateway info endpoints ────────────────────────────────────────
        location = / {
            default_type application/json;
            return 200 '{"name":"Hdos API Gateway (nginx)","routes":["/auth/*","/orders/*","/notifications/*","/m01/*"]}';
        }

        location = /health {
            default_type application/json;
            return 200 '{"status":"OK","service":"nginx-gateway"}';
        }

        # ── Internal JWT validation endpoint ─────────────────────────────
        # Dùng bởi auth_request directive. Không thể gọi từ ngoài (internal).
        # nginx gọi endpoint này khi có request vào protected route.
        # AuthService.ValidateToken() kiểm tra JWT và trả 200/401.
        location = /_auth_validate {
            internal;                           # Chỉ nginx gọi được, không phải client
            proxy_pass              http://authservice/auth/validate;
            proxy_pass_request_body off;        # Không forward body (GET request)
            proxy_set_header        Content-Length   "";
            proxy_set_header        X-Original-URI   $request_uri;  # Cho service biết URI gốc
        }

        # ── Auth Service ──────────────────────────────────────────────────
        # Tất cả anonymous (login, register, validate đều cần không hoặc có token)
        location /auth/ {
            proxy_pass         http://authservice;
            proxy_set_header   Host              $host;
            proxy_set_header   X-Real-IP         $remote_addr;
            proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
            proxy_set_header   X-Forwarded-Proto $scheme;
            proxy_http_version 1.1;
            proxy_set_header   Upgrade    $http_upgrade;    # WebSocket support
            proxy_set_header   Connection $connection_upgrade;
        }

        # ── Orders ───────────────────────────────────────────────────────
        # Health check: strip prefix /orders → orderservice nhận /health/live
        # Ví dụ: GET /orders/health/live → orderservice GET /health/live
        location ~ ^/orders(/health.*) {
            proxy_pass         http://orderservice$1;   # $1 = capture group = /health.*
            proxy_set_header   Host            $host;
            proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_http_version 1.1;
        }

        # Swagger: anonymous, giữ nguyên path
        # /orders/swagger → orderservice GET /orders/swagger (RoutePrefix đã config)
        location /orders/swagger {
            proxy_pass         http://orderservice;
            proxy_set_header   Host            $host;
            proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_http_version 1.1;
        }

        # Business routes: yêu cầu JWT hợp lệ
        location /orders/ {
            auth_request /_auth_validate;   # Gọi AuthService trước
            error_page 401 = @unauthorized;
            error_page 403 = @forbidden;

            proxy_pass         http://orderservice;
            proxy_set_header   Host              $host;
            proxy_set_header   X-Real-IP         $remote_addr;
            proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
            proxy_set_header   X-Forwarded-Proto $scheme;
            proxy_http_version 1.1;
            proxy_set_header   Upgrade    $http_upgrade;
            proxy_set_header   Connection $connection_upgrade;
        }

        # ── Notifications ────────────────────────────────────────────────
        # Cùng pattern với orders: health, swagger (anonymous), business (JWT)
        location ~ ^/notifications(/health.*) {
            proxy_pass         http://notificationservice$1;
            proxy_set_header   Host            $host;
            proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_http_version 1.1;
        }

        location /notifications/swagger {
            proxy_pass         http://notificationservice;
            proxy_set_header   Host            $host;
            proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_http_version 1.1;
        }

        location /notifications/ {
            auth_request /_auth_validate;
            error_page 401 = @unauthorized;
            error_page 403 = @forbidden;

            proxy_pass         http://notificationservice;
            proxy_set_header   Host              $host;
            proxy_set_header   X-Real-IP         $remote_addr;
            proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
            proxy_set_header   X-Forwarded-Proto $scheme;
            proxy_http_version 1.1;
            proxy_set_header   Upgrade    $http_upgrade;
            proxy_set_header   Connection $connection_upgrade;
        }

        # ── M01 Service ──────────────────────────────────────────────────
        location ~ ^/m01(/health.*) {
            proxy_pass         http://m01service$1;
            proxy_set_header   Host            $host;
            proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_http_version 1.1;
        }

        location /m01/swagger {
            proxy_pass         http://m01service;
            proxy_set_header   Host            $host;
            proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_http_version 1.1;
        }

        location /m01/ {
            auth_request /_auth_validate;
            error_page 401 = @unauthorized;
            error_page 403 = @forbidden;

            proxy_pass         http://m01service;
            proxy_set_header   Host              $host;
            proxy_set_header   X-Real-IP         $remote_addr;
            proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
            proxy_set_header   X-Forwarded-Proto $scheme;
            proxy_http_version 1.1;
            proxy_set_header   Upgrade    $http_upgrade;
            proxy_set_header   Connection $connection_upgrade;
        }

        # ── Error JSON responses ──────────────────────────────────────────
        # Trả JSON thay vì HTML của nginx mặc định
        location @unauthorized {
            default_type application/json;
            return 401 '{"error":"Unauthorized","message":"Valid JWT token required"}';
        }

        location @forbidden {
            default_type application/json;
            return 403 '{"error":"Forbidden","message":"Insufficient permissions"}';
        }
    }
}
```

---

## Cách auth_request hoạt động

```
Client: GET /m01/dashboard/summary
        Authorization: Bearer eyJhbGci...
         │
         ▼
      nginx (location /m01/)
         │
         ├── 1. auth_request /_auth_validate
         │         │
         │         └── nginx gửi subrequest:
         │             GET http://authservice/auth/validate
         │             (kèm Authorization header từ request gốc)
         │                  │
         │                  ▼
         │             AuthService.ValidateToken()
         │             [Authorize] → JWT validation
         │                  │
         │             ┌────┴─────┐
         │             200        401
         │             OK         Unauthorized
         │             │          │
         ├─────────────┘          └── nginx trả 401 @unauthorized
         │
         └── 2. Nếu 200: proxy request đến m01service
                   GET http://m01service/m01/dashboard/summary
                   (kèm tất cả headers gốc)
```

**Lưu ý quan trọng:** `auth_request` nhận response code:
- `2xx` → cho đi tiếp
- `401` → trả 401 về client (qua `@unauthorized` named location)
- `403` → trả 403 về client
- Bất kỳ code nào khác (404, 500...) → nginx trả **500** về client

Đây là lý do tại sao endpoint `/auth/validate` phải tồn tại và trả 200 (JWT hợp lệ) hoặc 401 (JWT không hợp lệ) — không được trả 404.

---

## Swagger với prefix routing

Mỗi service cấu hình Swagger `RoutePrefix` khớp với nginx prefix:

```csharp
// Trong Program.cs của mỗi service:
app.UseSwagger(c => c.RouteTemplate = "orders/swagger/{documentName}/swagger.json");
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/orders/swagger/v1/swagger.json", "OrderService v1");
    c.RoutePrefix = "orders/swagger";   // Swagger UI serve tại /orders/swagger
});
```

Vì vậy khi truy cập `http://server:5000/orders/swagger`:
1. nginx nhận → match `location /orders/swagger` → forward tới orderservice
2. orderservice nhận `/orders/swagger` → Swashbuckle phục vụ Swagger UI (vì RoutePrefix = "orders/swagger")
3. HTML tải → fetch assets và JSON spec tại `/orders/swagger/...` → nginx forward tiếp

---

## Deploy nginx config mới

nginx dùng volume mount — không cần rebuild image:

```yaml
# docker-compose.yml
nginx:
  image: nginx:1.27-alpine
  volumes:
    - ./nginx/nginx.conf:/etc/nginx/nginx.conf:ro   # Mount từ host
```

Quy trình update:
```bash
# 1. Sửa nginx/nginx.conf
# 2. Kiểm tra syntax (optional)
docker exec hdos-nginx-1 nginx -t

# 3. Reload không downtime
docker exec hdos-nginx-1 nginx -s reload

# Hoặc nếu cần đổi config lớn (thêm upstream...):
docker restart hdos-nginx-1
```

**Lưu ý:** `nginx -s reload` reload config mà không đóng existing connections. `docker restart` có brief downtime (~1 giây).

---

## Thêm service mới

1. Thêm upstream:
```nginx
upstream newservice { server newservice:8080; }
```

2. Thêm routes (copy pattern của service hiện có):
```nginx
location ~ ^/new(/health.*) {
    proxy_pass http://newservice$1;
    ...
}

location /new/swagger { proxy_pass http://newservice; ... }

location /new/ {
    auth_request /_auth_validate;
    error_page 401 = @unauthorized;
    proxy_pass http://newservice;
    ...
}
```

3. Reload: `docker exec hdos-nginx-1 nginx -s reload`
