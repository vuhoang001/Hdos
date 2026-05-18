# 16 — HTTPS, Keycloak Proxy & Issuer Management

## Tóm tắt nhanh

| Vấn đề | Nguyên nhân | Giải pháp |
|--------|------------|-----------|
| `Web Crypto API is not available` | FE truy cập qua HTTP (non-localhost) | Thêm HTTPS vào nginx (self-signed cert) |
| Keycloak JS bị Mixed Content blocked | FE ở HTTPS gọi Keycloak HTTP `:8080` | Proxy `/realms/` `/resources/` `/js/` qua nginx HTTPS |
| JWT 401 trên server | Issuer trong token ≠ `Keycloak__Authority` trong services | KC_HOSTNAME_URL + MetadataAddress pattern |
| HTTP 500 trên `/auth/validate` | Duplicate email khi Keycloak sub UUID đổi | Lookup by email trước khi insert user mới |

---

## 1. Vấn đề gốc rễ và chuỗi lỗi

### 1.1 Web Crypto API

Trình duyệt hiện đại chặn `window.crypto.subtle` (dùng cho PKCE S256) trên **mọi trang không phải secure context**. Secure context = HTTPS hoặc `localhost`.

```
Người dùng vào http://192.168.100.60:4000  (HTTP, non-localhost)
  → Keycloak JS: kc.init({ pkceMethod: 'S256' })
  → window.crypto.subtle.digest(...)
  → BLOCKED: insecure context
  → Throw "Web Crypto API is not available"
```

**Fix lần 1:** Thêm HTTPS vào nginx. User vào `https://IP:8443` → secure context → Web Crypto hoạt động.

### 1.2 Mixed Content

Sau khi có HTTPS, FE ở `https://192.168.100.60:8443` vẫn lỗi vì Keycloak JS cần gọi Keycloak Admin/Token endpoints qua XHR:

```
FE tại https://192.168.100.60:8443
  → Keycloak JS: fetch("http://192.168.100.60:8080/realms/hdos/.well-known/...")
  → BLOCKED: Mixed Content (HTTPS page → HTTP fetch)
```

**Fix lần 2:** Proxy Keycloak qua nginx HTTPS. FE gọi `https://IP:8443/realms/...` → nginx forward đến `http://keycloak:8080/realms/...` nội bộ.

### 1.3 Issuer Mismatch

Khi proxy Keycloak qua nginx, Keycloak phải biết URL "thật" của nó để điền `iss` (issuer) vào token đúng:

```
Trước: Keycloak không biết proxy → iss = "http://192.168.100.60:8080/realms/hdos"
       Services expect             = "http://192.168.100.60:8080/realms/hdos"   ✅

Sau khi proxy qua nginx cần:
       iss = "https://192.168.100.60:8443/realms/hdos"  (FE verify được)
       Services expect             = "https://192.168.100.60:8443/realms/hdos"  ✅

Nếu không đồng bộ → JWT 401 ở mọi endpoint
```

**Fix lần 3:** `KC_HOSTNAME_URL=https://IP:8443` + cập nhật `Keycloak__Authority` trong tất cả services.

### 1.4 JWKS Fetch từ trong Docker

Services fetch JWKS (public keys để verify JWT) từ URL trong discovery document. Nếu Authority là `https://IP:8443/realms/hdos`, service sẽ cố gọi đến nginx qua HTTPS — nhưng cert self-signed → .NET HttpClient reject.

```
authservice (trong Docker)
  → HTTPS discovery: https://192.168.100.60:8443/realms/hdos/.well-known/...
  → TLS handshake → self-signed cert → REJECTED
  → Service không load được JWKS → tất cả JWT fail
```

**Fix lần 4:** `MetadataAddress` — cho phép tách biệt **URL để validate issuer** và **URL để fetch JWKS**:

```
Authority       = https://192.168.100.60:8443/realms/hdos   (validate iss claim)
MetadataAddress = http://keycloak:8080/realms/hdos/.well-known/...  (fetch JWKS - nội bộ, HTTP)
```

---

## 2. Kiến trúc sau thay đổi

```
                    ┌──────────────────────────────────────────────┐
                    │              nginx container                  │
Browser             │  port 8080 → redirect 301 https://$host      │
  │                 │                                               │
  ├─ http:5000 ───▶ │  port 8443 (SSL, self-signed cert)           │
  │                 │                                               │
  └─ https:8443 ──▶ │  /realms/*  → keycloak:8080  (proxy)         │
                    │  /resources/* → keycloak:8080  (proxy)        │
                    │  /js/*      → keycloak:8080  (proxy)          │
                    │  /auth/*    → authservice:8080                │
                    │  /orders/*  → orderservice:8080               │
                    │  /notif.*   → notificationservice:8080        │
                    │  /m01/*     → m01service:8080                 │
                    │  /async/*   → asyncgateway:8080               │
                    │  /          → frontend:4000                   │
                    └──────────────────────────────────────────────┘

Keycloak (keycloak:8080)
  KC_HOSTNAME_URL = https://192.168.100.60:8443
  KC_PROXY = edge
  → iss trong token = "https://192.168.100.60:8443/realms/hdos"

Services (.NET)
  Authority       = https://192.168.100.60:8443/realms/hdos  ← validate iss
  MetadataAddress = http://keycloak:8080/realms/hdos/.well-known/...  ← fetch JWKS

Docker volume hdos-nginx-ssl:
  /etc/nginx/ssl/hdos.key
  /etc/nginx/ssl/hdos.crt   (self-signed, 10 năm, SAN: localhost + IP)
```

---

## 3. Files thay đổi

### 3.1 `nginx/nginx.conf`

**Thêm upstream keycloak:**
```nginx
upstream keycloak { server keycloak:8080; }
```

**Thêm proxy locations (trong HTTPS server block, trước `location /`):**
```nginx
# Keycloak proxy — browser gọi HTTPS, nginx forward HTTP nội bộ
location /realms/ {
    proxy_pass         http://keycloak/realms/;
    proxy_set_header   X-Forwarded-Proto $scheme;
    proxy_set_header   X-Forwarded-Port  $server_port;
}

location /resources/ {
    proxy_pass         http://keycloak/resources/;
    proxy_set_header   X-Forwarded-Proto $scheme;
}

location /js/ {
    proxy_pass         http://keycloak/js/;
    proxy_set_header   X-Forwarded-Proto $scheme;
}
```

`X-Forwarded-Proto: https` báo cho Keycloak biết nó đang đứng sau HTTPS proxy → Keycloak tạo redirect URL đúng.

---

### 3.2 `docker-compose.server.yml`

**Keycloak:**
```yaml
keycloak:
  environment:
    KC_HOSTNAME_URL: "https://${SERVER_IP}:8443"   # ← issuer = https://...
    KC_HTTP_PORT: "8080"
    KC_PROXY: "edge"          # ← Keycloak biết mình đứng sau reverse proxy
    KC_HOSTNAME_STRICT: "false"
```

**Mỗi service (.NET):**
```yaml
authservice:
  environment:
    Keycloak__Authority: "https://${SERVER_IP}:8443/realms/hdos"
    Keycloak__MetadataAddress: "http://keycloak:8080/realms/hdos/.well-known/openid-configuration"
```

> `SERVER_IP` phải có trong `/opt/hdos-prod/.env` (hoặc `/opt/hdos-staging/.env`).

---

### 3.3 `src/BuildingBlocks/Common/Auth/KeycloakOptions.cs`

```csharp
public sealed class KeycloakOptions
{
    public string Authority      { get; set; } = string.Empty;
    public string Audience       { get; set; } = "hdos-backend";

    // Optional: URL nội bộ để fetch JWKS, tách biệt với Authority.
    // Dùng khi Authority là HTTPS public URL nhưng service cần tránh TLS cert
    // self-signed khi đứng trong Docker network.
    public string MetadataAddress { get; set; } = string.Empty;
}
```

---

### 3.4 `src/BuildingBlocks/Common/Auth/JwtAuthExtensions.cs`

```csharp
o.Authority           = opts.Authority;
o.Audience            = opts.Audience;
o.RequireHttpsMetadata = false;

if (!string.IsNullOrWhiteSpace(opts.MetadataAddress))
    o.MetadataAddress = opts.MetadataAddress;
```

Khi `MetadataAddress` được set:
- .NET dùng URL này để gọi `/.well-known/openid-configuration` → lấy JWKS
- `.Authority` vẫn dùng để **validate `iss` claim** trong JWT

---

### 3.5 `src/Services/AuthService/…/ValidateAndResolveQuery.cs`

**Bug đã fix:** Keycloak sub UUID có thể thay đổi khi user bị xóa và tạo lại trong Keycloak. `GetByIdAsync` trả về null → service cố insert user mới → `Email UNIQUE` constraint violation → SQL Error 2601 → HTTP 500 trên **tất cả** endpoint protected bởi `auth_request`.

```csharp
// Trước (bug):
var user = await users.GetByIdAsync(request.UserId, ct);
if (user is null)
{
    user = User.Provision(request.UserId, ...);
    await users.AddAsync(user, ct);
    await uow.SaveChangesAsync(ct);   // ← CRASH nếu email đã tồn tại với UUID khác
}

// Sau (fix):
var user = await users.GetByIdAsync(request.UserId, ct);
if (user is null)
{
    var existingByEmail = await users.GetByEmailAsync(email, ct);
    if (existingByEmail is not null)
    {
        // Keycloak sub đổi (user recreated) — dùng lại user cũ
        user = existingByEmail;
        user.UpdateLastSeen();
        users.Update(user);
        await uow.SaveChangesAsync(ct);
    }
    else
    {
        // User thật sự mới
        user = User.Provision(request.UserId, ...);
        await users.AddAsync(user, ct);
        await uow.SaveChangesAsync(ct);
        await eventBus.PublishAsync(new UserRegisteredIntegrationEvent(...), ct);
    }
}
```

---

### 3.6 `nginx/entrypoint.sh` *(tạo mới)*

Auto-generate self-signed TLS cert khi container khởi động lần đầu:

```sh
#!/bin/sh
set -e
SSL_DIR=/etc/nginx/ssl

if [ ! -f "$SSL_DIR/hdos.key" ] || [ ! -f "$SSL_DIR/hdos.crt" ]; then
    command -v openssl >/dev/null 2>&1 || apk add --no-cache openssl
    openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
        -keyout "$SSL_DIR/hdos.key" -out "$SSL_DIR/hdos.crt" \
        -subj "/CN=hdos-dev/O=Hdos Dev/C=VN" \
        -addext "subjectAltName=DNS:localhost,DNS:hdos-nginx,IP:127.0.0.1"
fi

exec nginx -g "daemon off;"
```

> **Lưu ý:** `nginx:1.27-alpine` chỉ có `libssl3` (thư viện) chứ KHÔNG có `openssl` CLI. Script phải `apk add openssl` trước khi dùng.

---

## 4. Cấu hình Frontend (Next.js)

FE phải trỏ Keycloak URL về nginx HTTPS (không phải port 8080):

```env
# .env.local (local dev — chạy qua localhost)
NEXT_PUBLIC_KEYCLOAK_URL=http://localhost:8080
NEXT_PUBLIC_KEYCLOAK_REALM=hdos
NEXT_PUBLIC_KEYCLOAK_CLIENT_ID=hdos-frontend
NEXT_PUBLIC_API_URL=https://localhost:8443

# .env.production (server — Keycloak qua nginx proxy)
NEXT_PUBLIC_KEYCLOAK_URL=https://192.168.100.60:8443
NEXT_PUBLIC_KEYCLOAK_REALM=hdos
NEXT_PUBLIC_KEYCLOAK_CLIENT_ID=hdos-frontend
NEXT_PUBLIC_API_URL=https://192.168.100.60:8443
```

Khởi tạo Keycloak JS:

```typescript
const kc = new Keycloak({
  url:      process.env.NEXT_PUBLIC_KEYCLOAK_URL,  // https://IP:8443 trên server
  realm:    'hdos',
  clientId: 'hdos-frontend',
});

await kc.init({
  onLoad:     'check-sso',
  pkceMethod: 'S256',   // Web Crypto hoạt động vì HTTPS context
});
```

---

## 5. Hướng dẫn chạy

### 5.1 Local dev

```bash
docker compose up -d

# Test
curl -k https://localhost:8443/health
curl -k https://localhost:8443/realms/hdos/.well-known/openid-configuration | python3 -m json.tool | grep issuer
# Kỳ vọng: "issuer": "http://localhost:8080/realms/hdos"  (local dùng KC_HOSTNAME mặc định)
```

### 5.2 Server (staging / production)

```bash
# Đảm bảo /opt/hdos-prod/.env có SERVER_IP
echo "SERVER_IP=192.168.100.60" >> /opt/hdos-prod/.env

# Deploy (CI/CD tự làm, hoặc thủ công)
docker compose -f docker-compose.yml -f docker-compose.server.yml \
  --env-file /opt/hdos-prod/.env up -d

# Kiểm tra issuer đúng
curl -sk https://192.168.100.60:8443/realms/hdos/.well-known/openid-configuration \
  | python3 -c 'import sys,json; print(json.load(sys.stdin)["issuer"])'
# Kỳ vọng: https://192.168.100.60:8443/realms/hdos

# Test auth flow
curl -sk https://192.168.100.60:8443/auth/health
# Kỳ vọng: {"status":"OK","service":"AuthService",...}
```

### 5.3 Trust cert trên browser

Lần đầu vào `https://192.168.100.60:8443`:

- **Chrome/Edge**: Click **Advanced** → **Proceed to 192.168.100.60 (unsafe)**
- **Firefox**: Click **Advanced...** → **Accept the Risk and Continue**

Sau đó Keycloak login, SignalR, API call đều hoạt động bình thường.

---

## 6. Điểm mạnh của thiết kế hiện tại

| # | Điểm mạnh | Lý do |
|---|-----------|-------|
| 1 | **Zero-config TLS cho dev** | `entrypoint.sh` tự gen cert, không cần setup thủ công |
| 2 | **Một điểm vào duy nhất** | Browser, FE, API đều qua port 8443 — không lộ port nội bộ |
| 3 | **MetadataAddress tách issuer và JWKS** | Services validate issuer qua HTTPS URL nhưng fetch JWKS qua HTTP nội bộ — tránh TLS cert issue trong Docker |
| 4 | **KC_PROXY=edge** | Keycloak tạo redirect URL đúng dù đứng sau proxy — không bị loop redirect |
| 5 | **Email fallback trong JIT provisioning** | AuthService không crash khi Keycloak sub UUID đổi (user tạo lại) |
| 6 | **CORS whitelist** | Chỉ reflect origin nằm trong whitelist (`localhost:*`, `192.168.*`) — không echo-all |

---

## 7. Điểm yếu & việc cần refactor

### 7.1 Self-signed cert — trình duyệt không tin tự động

**Vấn đề:** User phải click "Proceed anyway" một lần. Trong production thực tế, cert này sẽ bị reject hoàn toàn (e.g., mobile app, API client tự động).

**Refactor:** Dùng Let's Encrypt nếu có domain, hoặc cert từ tổ chức. Xem `§ 8. Production`.

---

### 7.2 HTTP redirect sang HTTPS chỉ đúng khi user vào đúng port

**Vấn đề:** HTTP redirect (`http://IP:5000` → `https://$host`) sẽ redirect sang `https://IP` (port 443), không phải `https://IP:8443`. User phải vào thẳng `https://IP:8443`.

**Refactor:**
```nginx
# Sửa redirect để chỉ rõ port
return 301 https://$host:8443$request_uri;
```
Hoặc map nginx ra port 443 trên host (cần check xem port 443 có bị chiếm chưa).

---

### 7.3 SERVER_IP hardcode trong config

**Vấn đề:** `KC_HOSTNAME_URL` và `Keycloak__Authority` chứa IP server. Khi chuyển server hoặc thêm domain, phải đổi ở nhiều chỗ.

**Refactor:** Dùng domain thật + Let's Encrypt. `KC_HOSTNAME_URL=https://app.example.com` → không bao giờ phải sửa khi chuyển IP.

---

### 7.4 Keycloak sub UUID mismatch vẫn còn silent failure

**Vấn đề:** Khi Keycloak sub UUID đổi, AuthService dùng user cũ (tìm theo email). Nhưng user ID trong DB không được update theo UUID Keycloak mới. Các request sau sẽ luôn phải hit `GetByEmailAsync` thay vì `GetByIdAsync` — tốn thêm 1 query mỗi request.

**Refactor:** Khi phát hiện UUID mismatch, cập nhật `User.Id` trong DB (hoặc log warning và tạo mapping table `KeycloakSubMapping`).

---

### 7.5 Không có cert rotation tự động

**Vấn đề:** Self-signed cert hết hạn sau 10 năm. Không có cơ chế tự gia hạn.

**Refactor:** Dùng Let's Encrypt + `certbot renew` (cron), hoặc cert-manager nếu chuyển lên Kubernetes.

---

### 7.6 Keycloak proxy không có cache

**Vấn đề:** Mỗi request đến `/realms/...` đều proxy qua nginx đến Keycloak. Static resources như Keycloak JS (`/js/keycloak.min.js`) không được cache ở nginx.

**Refactor:**
```nginx
location /js/ {
    proxy_pass http://keycloak/js/;
    proxy_cache_valid 200 7d;
    add_header Cache-Control "public, max-age=604800";
}
```

---

### 7.7 Roles trong AuthService DB phải seed thủ công

**Vấn đề:** Không có seed data — `Roles`, `Permissions`, `RolePermissions` trống sau khi khởi động. Phải insert bằng SQL hoặc Admin API. User đăng nhập thành công nhưng nhận 403 vì không có permission nào.

**Refactor:** Thêm migration seed data với các role + permission cơ bản. Hoặc viết endpoint `POST /auth/admin/seed` (chỉ chạy 1 lần khi DB mới).

---

## 8. Production — cert thật

### Option A: Let's Encrypt (có domain)

```bash
certbot certonly --standalone -d yourdomain.com
```

Trong `docker-compose.yml`, bỏ `entrypoint` override, mount cert trực tiếp:

```yaml
nginx:
  command: ["nginx", "-g", "daemon off;"]
  volumes:
    - ./nginx/nginx.conf:/etc/nginx/nginx.conf:ro
    - /etc/letsencrypt/live/yourdomain.com/fullchain.pem:/etc/nginx/ssl/hdos.crt:ro
    - /etc/letsencrypt/live/yourdomain.com/privkey.pem:/etc/nginx/ssl/hdos.key:ro
```

Trong `docker-compose.server.yml`:
```yaml
keycloak:
  environment:
    KC_HOSTNAME_URL: "https://yourdomain.com"
```

Và cập nhật `SERVER_IP` → `SERVER_DOMAIN` trong `.env`.

### Option B: Cert từ tổ chức / mua

Đặt file `.crt` và `.key` vào thư mục an toàn trên server, mount tương tự Option A.

---

## 9. Xử lý sự cố

### JWT 401 trên server sau deploy

```bash
# Kiểm tra issuer trong discovery document
curl -sk https://<SERVER_IP>:8443/realms/hdos/.well-known/openid-configuration \
  | python3 -c 'import sys,json; print(json.load(sys.stdin)["issuer"])'

# Phải khớp với Keycloak__Authority trong services
docker exec hdos-authservice-1 env | grep Keycloak__Authority
```

Nếu không khớp → kiểm tra `SERVER_IP` trong `.env`, restart keycloak trước, rồi services.

---

### HTTP 500 trên `/auth/validate`

Kiểm tra authservice logs:

```bash
docker logs hdos-authservice-1 --tail 50 | grep -i "error\|exception"
```

- **SQL Error 2601** (duplicate key): Email đã tồn tại với UUID khác → đã fix, nếu vẫn xảy ra xem `§ 7.4`
- **Connection refused to keycloak**: MetadataAddress không resolve → kiểm tra `Keycloak__MetadataAddress`

---

### Keycloak redirect về URL sai

```bash
# Xem KC_HOSTNAME_URL đang được set gì
docker exec hdos-keycloak env | grep KC_HOSTNAME
```

Nếu rỗng hoặc sai → kiểm tra `.env` trên server có `SERVER_IP` chưa.

---

### Port 8443 không mở

```bash
docker ps | grep nginx          # container có đang chạy không?
docker logs hdos-nginx --tail 20 # xem log cert generation
curl -sk https://localhost:8443/health
```

---

## Tham khảo

- [Keycloak 24 — Hostname Configuration](https://www.keycloak.org/server/hostname)
- [Keycloak 24 — Reverse Proxy](https://www.keycloak.org/server/reverseproxy)
- [MDN — Secure Contexts](https://developer.mozilla.org/en-US/docs/Web/Security/Secure_Contexts)
- [MDN — Mixed Content](https://developer.mozilla.org/en-US/docs/Web/Security/Mixed_content)
- [.NET JwtBearerOptions.MetadataAddress](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.jwtbearer.jwtbeareroptions.metadataaddress)
- [Let's Encrypt — Getting Started](https://letsencrypt.org/getting-started/)
