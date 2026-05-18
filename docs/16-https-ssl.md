# 16 — HTTPS / TLS cho Nginx Gateway

## 1. Tại sao cần HTTPS?

Trình duyệt hiện đại (Chrome 58+, Firefox 75+) chặn **Web Crypto API** (`window.crypto.subtle`) trên mọi trang HTTP không phải `localhost`. Keycloak JS adapter dùng API này để tính PKCE challenge (S256). Kết quả:

```
Uncaught Error: Web Crypto API is not available.
  at createLoginUrl ...
```

**Root cause chuỗi lỗi:**

```
Frontend truy cập qua http://IP-hoặc-domain
    → Keycloak JS gọi kc.login()
    → Tạo PKCE code_challenge (S256)
    → Gọi window.crypto.subtle.digest()
    → Trình duyệt BLOCK (insecure context)
    → Throw "Web Crypto API is not available"
```

**Giải pháp:** Thêm HTTPS vào nginx. Cert self-signed đủ để fix lỗi này trong dev/staging — trình duyệt chấp nhận sau khi user click "Proceed anyway" một lần.

---

## 2. Kiến trúc sau thay đổi

```
                        ┌─────────────────────────┐
Browser                 │     nginx container      │
  │                     │                          │
  ├─ http://IP:5000 ──► │ port 8080                │
  │                     │   return 301 https://$host│
  │                     │                          │
  └─ https://IP ──────► │ port 8443 (SSL)          │
                        │   ssl_certificate hdos.crt│
                        │   → proxy các services   │
                        └─────────────────────────┘

Docker volume hdos-nginx-ssl (cert tồn tại qua restart):
  /etc/nginx/ssl/
    hdos.key   ← private key
    hdos.crt   ← self-signed cert (10 năm, SAN: localhost + 127.0.0.1)
```

---

## 3. Files thay đổi

### `nginx/entrypoint.sh` *(file mới)*

Script thay thế lệnh khởi động mặc định của nginx container. Mỗi lần container start, script này chạy trước:

```sh
#!/bin/sh
set -e

SSL_DIR=/etc/nginx/ssl
KEY="$SSL_DIR/hdos.key"
CERT="$SSL_DIR/hdos.crt"

if [ ! -f "$KEY" ] || [ ! -f "$CERT" ]; then
    # Tạo cert mới (chỉ lần đầu hoặc khi volume bị xóa)
    openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
        -keyout "$KEY" -out "$CERT" \
        -subj   "/CN=hdos-dev/O=Hdos Dev/C=VN" \
        -addext "subjectAltName=DNS:localhost,DNS:hdos-nginx,IP:127.0.0.1"
fi

exec nginx -g "daemon off;"
```

Cert được lưu trong Docker volume `hdos-nginx-ssl` → **không generate lại mỗi lần restart**, chỉ generate khi volume chưa có cert.

---

### `nginx/nginx.conf` *(thêm 2 server block)*

**Trước** — 1 server block trên port 8080 làm tất cả.

**Sau** — tách thành 2 block:

```nginx
# Block 1: HTTP → HTTPS redirect
server {
    listen 8080;
    server_name _;
    return 301 https://$host$request_uri;
}

# Block 2: HTTPS — toàn bộ API Gateway logic
server {
    listen 8443 ssl;
    ssl_certificate     /etc/nginx/ssl/hdos.crt;
    ssl_certificate_key /etc/nginx/ssl/hdos.key;
    ssl_protocols       TLSv1.2 TLSv1.3;
    ssl_ciphers         HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers on;
    ssl_session_cache   shared:SSL:10m;
    ssl_session_timeout 10m;

    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;

    # ... toàn bộ proxy config, CORS, auth_request như cũ ...
}
```

`Strict-Transport-Security` (HSTS) buộc trình duyệt luôn dùng HTTPS sau lần truy cập đầu tiên — ngăn downgrade attack.

---

### `docker-compose.yml` *(nginx service)*

```yaml
nginx:
  image: nginx:1.27-alpine
  container_name: hdos-nginx
  entrypoint: ["/bin/sh", "/etc/nginx/entrypoint.sh"]   # ← override
  volumes:
    - ./nginx/nginx.conf:/etc/nginx/nginx.conf:ro
    - ./nginx/entrypoint.sh:/etc/nginx/entrypoint.sh:ro  # ← mount script
    - hdos-nginx-ssl:/etc/nginx/ssl                       # ← volume lưu cert
  ports:
    - "5000:8080"    # HTTP  → redirect sang HTTPS
    - "443:8443"     # HTTPS → API Gateway

volumes:
  hdos-nginx-ssl:    # named volume, tồn tại qua docker compose down/up
```

Port mapping sau thay đổi:

| Host port | Container port | Mục đích |
|-----------|---------------|----------|
| 5000 | 8080 | HTTP (chỉ redirect 301) |
| **443** | **8443** | **HTTPS — API Gateway chính** |

---

## 4. Hướng dẫn chạy

### Lần đầu (hoặc sau khi clone)

```bash
# 1. Kéo code mới nhất
git pull

# 2. Khởi động lại nginx (rebuild entrypoint + config)
docker compose up -d --build nginx

# Hoặc khởi động toàn bộ stack lần đầu
docker compose up -d
```

Khi nginx container start, bạn sẽ thấy log:

```
[nginx] Generating self-signed TLS certificate...
[nginx] Certificate ready.
```

Những lần sau:

```
[nginx] Certificate already exists, skipping.
```

---

### Kiểm tra HTTPS hoạt động

```bash
# Kiểm tra redirect HTTP → HTTPS
curl -v http://localhost:5000/health
# Kỳ vọng: HTTP/1.1 301 Moved Permanently
# Location: https://localhost/health

# Kiểm tra HTTPS (bỏ qua verify cert vì self-signed)
curl -k https://localhost/health
# Kỳ vọng: {"status":"OK","service":"nginx-gateway"}

# Kiểm tra TLS version
openssl s_client -connect localhost:443 -tls1_2 < /dev/null 2>&1 | grep "Protocol"
# Kỳ vọng: Protocol  : TLSv1.2

openssl s_client -connect localhost:443 -tls1_3 < /dev/null 2>&1 | grep "Protocol"
# Kỳ vọng: Protocol  : TLSv1.3
```

---

### Xem cert đang dùng

```bash
# Xem thông tin cert
openssl s_client -connect localhost:443 < /dev/null 2>/dev/null \
  | openssl x509 -noout -text | grep -E "Subject:|Validity|DNS:|IP:"
```

Kết quả mẫu:

```
Subject: CN=hdos-dev, O=Hdos Dev, C=VN
Validity
    Not Before: ...
    Not After : ... (10 năm)
DNS:localhost, DNS:hdos-nginx, IP Address:127.0.0.1
```

---

## 5. Cập nhật Frontend (Keycloak config)

Sau khi có HTTPS, cập nhật Keycloak client URL trong frontend:

```typescript
// Trước (HTTP — bị lỗi Web Crypto)
const kc = new Keycloak({
  url: 'http://192.168.x.x:8080',
  realm: 'hdos',
  clientId: 'hdos-frontend',
});

// Sau (HTTPS — Web Crypto hoạt động)
const kc = new Keycloak({
  url: 'https://192.168.x.x:8080',  // Keycloak vẫn HTTP, chỉ frontend cần HTTPS
  realm: 'hdos',
  clientId: 'hdos-frontend',
});

await kc.init({
  onLoad: 'check-sso',
  pkceMethod: 'S256',   // ← giờ hoạt động vì frontend chạy trên HTTPS
});
```

> **Lưu ý:** Keycloak (port 8080) không cần HTTPS trong dev. Chỉ cần **trang frontend** được serve qua HTTPS (hoặc `localhost`) thì Web Crypto mới hoạt động.

Nếu frontend gọi API qua nginx, đổi base URL:

```typescript
// Trước
const API_BASE = 'http://192.168.x.x:5000';

// Sau
const API_BASE = 'https://192.168.x.x';   // port 443 (mặc định, không cần ghi)
```

---

## 6. Trust cert self-signed trên trình duyệt

Lần đầu truy cập `https://IP-server`:

**Chrome / Edge:**
1. Trình duyệt hiện "Your connection is not private" (NET::ERR_CERT_AUTHORITY_INVALID)
2. Click **Advanced**
3. Click **Proceed to ... (unsafe)**
4. Từ lần sau trình duyệt nhớ và không hỏi lại (trong session)

**Firefox:**
1. Trình duyệt hiện "Warning: Potential Security Risk Ahead"
2. Click **Advanced...**
3. Click **Accept the Risk and Continue**

**curl (trong script/test):**
```bash
curl -k https://server/...     # -k = skip cert verify
# hoặc
curl --insecure https://server/...
```

---

## 7. Xử lý sự cố

### nginx không start — `bind() to 0.0.0.0:8443 failed (13: Permission denied)`

Port 443 trên host cần quyền root. Giải pháp:

```bash
# Kiểm tra xem có process nào chiếm port 443 chưa
sudo ss -tlnp | grep 443

# Nếu không có gì → docker cần chạy với sudo hoặc dùng port khác
# Đổi trong docker-compose.yml: "8443:8443" thay vì "443:8443"
# Rồi truy cập https://server:8443
```

---

### `[nginx] Generating self-signed TLS certificate...` lặp lại mỗi lần restart

Volume bị xóa hoặc không được mount đúng. Kiểm tra:

```bash
docker volume ls | grep nginx-ssl
docker volume inspect hdos_hdos-nginx-ssl
```

Nếu volume không tồn tại:

```bash
docker compose down
docker compose up -d   # volume sẽ được tạo lại và cert generate 1 lần
```

---

### HSTS khiến không thể truy cập HTTP nữa

Nếu đã từng truy cập HTTPS và muốn quay lại HTTP (xóa HSTS trong Chrome):

1. Vào `chrome://net-internals/#hsts`
2. Tìm domain trong **Delete domain security policies**
3. Click **Delete**

---

### Cert bị hết hạn (sau 10 năm, hoặc volume bị reset)

```bash
# Xóa cert cũ, nginx sẽ tự generate khi restart
docker volume rm hdos_hdos-nginx-ssl
docker compose restart nginx
```

---

## 8. Production — dùng cert thật

Self-signed cert chỉ dùng cho dev/staging. Production cần cert từ CA tin cậy.

### Option A: Let's Encrypt + Certbot (có domain public)

```bash
# Chạy certbot standalone (nginx phải tắt tạm)
certbot certonly --standalone -d yourdomain.com

# Cert được lưu tại:
# /etc/letsencrypt/live/yourdomain.com/fullchain.pem
# /etc/letsencrypt/live/yourdomain.com/privkey.pem
```

Mount vào docker-compose:

```yaml
nginx:
  volumes:
    - ./nginx/nginx.conf:/etc/nginx/nginx.conf:ro
    - /etc/letsencrypt/live/yourdomain.com/fullchain.pem:/etc/nginx/ssl/hdos.crt:ro
    - /etc/letsencrypt/live/yourdomain.com/privkey.pem:/etc/nginx/ssl/hdos.key:ro
  # KHÔNG cần entrypoint override nữa vì cert có sẵn
  entrypoint: []
  command: ["nginx", "-g", "daemon off;"]
```

Xóa `entrypoint` override và volume `hdos-nginx-ssl` khi dùng cert thật.

### Option B: Cert từ tổ chức / mua

Đặt file `.crt` và `.key` vào một thư mục an toàn, mount tương tự như trên.

---

## 9. Checklist deploy

- [ ] `docker compose up -d` chạy thành công
- [ ] `curl -k https://localhost/health` trả về `{"status":"OK"}`
- [ ] `curl -v http://localhost:5000/health` trả về `301`
- [ ] Trình duyệt mở `https://IP-server` → trust cert → thấy trang frontend
- [ ] Keycloak login không còn lỗi "Web Crypto API is not available"
- [ ] WebSocket (SignalR) kết nối qua `wss://` thay vì `ws://`

---

## Tham khảo

- [MDN — Secure Context](https://developer.mozilla.org/en-US/docs/Web/Security/Secure_Contexts)
- [Keycloak JS Adapter — PKCE](https://www.keycloak.org/docs/latest/securing_apps/#_javascript_adapter)
- [nginx SSL module docs](https://nginx.org/en/docs/http/ngx_http_ssl_module.html)
- [Let's Encrypt — Getting Started](https://letsencrypt.org/getting-started/)
