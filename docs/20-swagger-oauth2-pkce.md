# 20 — Swagger OAuth2 Authorization Code + PKCE (Keycloak)

Hướng dẫn test API qua Swagger UI với luồng đăng nhập **y hệt frontend**: bấm Authorize → redirect Keycloak login → quay về với token tự điền vào tất cả request.

---

## 1. Bối cảnh — tại sao có doc này

Trước đây dev có 3 cách lấy token để test API qua Swagger:

| Cách | Ưu | Nhược |
|------|----|-------|
| `POST /auth/login` (ROPC) | Nhanh, không cần CORS | Phải nhớ user/pass, ROPC bị Keycloak khuyến cáo chỉ dùng dev |
| Swagger OAuth2 Password flow | Không cần endpoint trung gian | Cần config CORS cho từng Swagger origin |
| **Auth Code + PKCE** *(doc này)* | Y hệt FE thật, an toàn, không lộ password vào backend | Phải redirect/popup |

Hệ thống giữ **cả ba**:

- **Auth Code + PKCE** (khuyến nghị) — flow này.
- **Bearer paste** — fallback cho khi muốn dán nhanh token từ nguồn khác.
- **`POST /auth/login`** — vẫn còn ở AuthService cho script/curl không có browser.

---

## 2. Cấu hình code

### 2.1 `SwaggerExtensions` (Building Block dùng chung)

File: `src/BuildingBlocks/Common/Swagger/SwaggerExtensions.cs`

`AddHdosSwagger(serviceName, keycloakPublicAuthority)` — khi truyền `keycloakPublicAuthority`, đăng ký **2 security scheme**:

1. `oauth2` — `AuthorizationCode` flow trỏ thẳng đến Keycloak:
   - `authorizationUrl = {publicAuthority}/protocol/openid-connect/auth`
   - `tokenUrl         = {publicAuthority}/protocol/openid-connect/token`
   - Scopes: `openid profile email`
2. `Bearer` — paste thủ công, fallback.

`UseHdosSwaggerUI(servicePrefix, serviceTitle)` — cấu hình UI:

```csharp
c.OAuthClientId("hdos-frontend");
c.OAuthUsePkce();
c.OAuthScopes("openid", "profile", "email");
```

> **Vì sao cần `keycloakPublicAuthority`?**
> Đây là URL mà **browser** phải reach được (qua nginx HTTPS), KHÔNG phải URL internal Docker. Nếu lấy nhầm internal URL (`http://keycloak:8080/...`), browser sẽ bị Mixed Content / DNS fail.

### 2.2 Mỗi service Program.cs

```csharp
builder.Services.AddHdosSwagger("OrderService", builder.Configuration["Keycloak:PublicAuthority"]);
// ...
app.UseHdosSwaggerUI("orders", "OrderService v1");
```

5 service đã wired: `AuthService`, `OrderService`, `NotificationService`, `M01Service`, `AsyncGateway` (ApiGateway).

### 2.3 `appsettings.json` (local)

```json
"Keycloak": {
  "Authority": "http://localhost:8080/realms/hdos",
  "PublicAuthority": "http://localhost:8080/realms/hdos"
}
```

Local thì cả 2 cùng URL — Keycloak chạy trực tiếp trên `8080`.

### 2.4 `docker-compose.server.yml` (server)

Trên server, browser truy cập Keycloak qua nginx HTTPS (`8443`), nên override env:

```yaml
authservice:
  environment:
    Keycloak__Authority: "https://${SERVER_IP}:8443/realms/hdos"
    Keycloak__MetadataAddress: "http://keycloak:8080/realms/hdos/.well-known/openid-configuration"
    Keycloak__PublicAuthority: "https://${SERVER_IP}:8443/realms/hdos"
```

- `Authority` — issuer mong đợi trong JWT (phải khớp issuer Keycloak ghi vào token = `KC_HOSTNAME_URL`).
- `MetadataAddress` — backend gọi `.well-known/openid-configuration` qua mạng internal Docker để lấy JWKs (nhanh, không phụ thuộc nginx).
- `PublicAuthority` — URL Swagger UI in vào HTML để browser redirect đến Keycloak login.

### 2.5 Keycloak client `hdos-frontend`

File: `keycloak/hdos-realm.json`

```json
{
  "clientId": "hdos-frontend",
  "publicClient": true,
  "standardFlowEnabled": true,
  "redirectUris": ["*"],
  "webOrigins": ["*"],
  "attributes": { "pkce.code.challenge.method": "S256" }
}
```

- `publicClient: true` — không có secret, dùng PKCE để bảo vệ code exchange.
- `redirectUris: ["*"]` — cho mọi callback URL của Swagger (`https://server-ip:8443/orders/swagger/oauth2-redirect.html`, ...).
- Trên prod thật, nên giới hạn rõ URL để giảm rủi ro.

---

## 3. Flow chi tiết

```
┌─ Browser ──────────────────────────────────────┐
│ 1. Mở https://server:8443/orders/swagger        │
│ 2. Bấm "Authorize" → chọn scheme "oauth2"       │
│ 3. Swagger sinh code_verifier + code_challenge  │
│    rồi redirect đến:                            │
│    {PublicAuthority}/protocol/openid-connect/   │
│      auth?client_id=hdos-frontend               │
│         &response_type=code                     │
│         &code_challenge=...                     │
│         &code_challenge_method=S256             │
│         &redirect_uri=.../oauth2-redirect.html  │
└─────────────────────────────────────────────────┘
                 │
                 ▼
┌─ Keycloak login form ──────────────────────────┐
│ 4. User nhập username/password                  │
│ 5. Keycloak verify → redirect về:               │
│    .../oauth2-redirect.html?code=XYZ            │
└─────────────────────────────────────────────────┘
                 │
                 ▼
┌─ Browser (Swagger) ────────────────────────────┐
│ 6. Swagger POST {PublicAuthority}/token         │
│      grant_type=authorization_code              │
│      code=XYZ                                   │
│      code_verifier=<original>                   │
│      client_id=hdos-frontend                    │
│ 7. Keycloak verify PKCE → trả access_token      │
│ 8. Swagger lưu token → mọi request kèm          │
│    "Authorization: Bearer <token>"              │
└─────────────────────────────────────────────────┘
```

---

## 4. Cách dùng (dev)

### Local

1. `docker compose up -d`
2. Mở `https://localhost:8443/orders/swagger` (chấp nhận self-signed cert).
3. Bấm **Authorize** → chọn block **oauth2** → check `openid profile email` → **Authorize**.
4. Keycloak login (`admin` / `Admin1234!` hoặc user đã seed).
5. Quay về Swagger → padlock đóng → call thử endpoint `[Authorize]` bất kỳ.

### Server (staging/production)

1. Mở `https://<SERVER_IP>:8443/orders/swagger`.
2. Flow giống local; redirect sẽ tự dùng `PublicAuthority` đã set trong `docker-compose.server.yml`.

> **Lưu ý**: trang Keycloak login sẽ chạy trên cùng host `SERVER_IP:8443` (qua nginx proxy `/realms/*`) → không bị Mixed Content.

---

## 5. Troubleshooting

| Triệu chứng | Nguyên nhân | Cách fix |
|-------------|-------------|----------|
| Bấm Authorize, redirect đến `http://localhost:8080/...` trong khi đang ở server | `Keycloak__PublicAuthority` chưa được set ở `docker-compose.server.yml` | Thêm env var, redeploy |
| Keycloak báo `Invalid parameter: redirect_uri` | URL callback của Swagger không nằm trong `redirectUris` của client | Sửa `keycloak/hdos-realm.json` (hoặc Admin Console) |
| Token lấy được nhưng API trả 401 | Issuer trong token ≠ `Keycloak:Authority` của service | Khớp lại `KC_HOSTNAME_URL` (Keycloak) với `Keycloak__Authority` (service) |
| Sau login redirect trở về Swagger nhưng padlock vẫn mở | Browser block 3rd-party cookie hoặc CSP chặn popup | Dùng same-site host (đã nginx proxy `/realms/*` chính là để giải) |
| Vẫn muốn dùng password grant nhanh | Không sao | Click Authorize → chọn scheme **Bearer**, paste token từ `POST /auth/login` |

---

## 6. Cross-reference

- [06 — Xác thực & Keycloak](./06-xac-thuc-va-keycloak.md) — chi tiết về realm, mapper, JIT user provisioning.
- [16 — HTTPS, Keycloak Proxy & Issuer](./16-https-ssl.md) — vì sao có 3 URL Authority/Metadata/Public, cách proxy Keycloak qua nginx.
- [05 — Nginx Gateway](./05-nginx-gateway.md) — block `location /realms/`.
