# 06 — Xác thực & Phân quyền

Hệ thống Hdos dùng **custom JWT auth** do `AuthService` tự phát hành (HS256 với shared secret). Trước đây dùng Keycloak — đã bỏ vì over-engineered cho nhu cầu hiện tại.

**Mô hình hiện tại (kể từ refactor 2026-05-20):** mọi việc check JWT + permission đều ở **services**, nginx chỉ làm reverse proxy + TLS + CORS. Permissions nằm thẳng trong JWT claims — không còn `auth_request` ở nginx, không còn `X-User-*` headers, không còn `/auth/validate`.

---

## 1. Tổng quan luồng

```
┌─ Frontend / Swagger ──────────────────────────────────────┐
│ POST /auth/login { email, password }                       │
│   → nginx (dumb proxy) → authservice                       │
│   ← 200 OK { token: "eyJhbGc..." }                         │
│     JWT chứa: sub, email, roles, permission[]              │
└────────────────────────────────────────────────────────────┘
                            │
                            ▼  (mọi request sau)
┌─ Browser ────────────────────────────────────────────────┐
│ POST /orders                                              │
│ Authorization: Bearer <token>                             │
└────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─ nginx ──────────────────────────────────────────────────┐
│ Proxy theo prefix (/orders, /m01, /notifications, /async) │
│ TLS termination + CORS authority — KHÔNG verify JWT       │
└────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─ orderservice ───────────────────────────────────────────┐
│ JwtBearer middleware verify HS256 + iss/aud/exp           │
│ ClaimsIdentity tự có claim "permission" từ JWT            │
│ [Authorize(Policy = "orders:create")] enforce             │
│   • Thiếu token   → 401 Unauthorized                      │
│   • Sai permission → 403 Forbidden                        │
└────────────────────────────────────────────────────────────┘
```

**Vì sao bỏ nginx auth_request?**

- Mỗi service đã verify JWT bằng cùng `JWT_SECRET` → nginx check thêm chỉ là duplicate.
- `auth_request` thêm 1 round-trip về AuthService cho mỗi request → AuthService thành SPOF.
- Permission đặt trong JWT giúp services stateless, không cần network call mỗi request.

**Trade-off:** permission thay đổi (admin gán/bỏ role) chỉ có hiệu lực sau khi user **login lại** hoặc token hết hạn (`ExpiresMinutes`, mặc định 8h). Cần realtime → giảm TTL hoặc implement refresh token + revocation list.

---

## 2. Cấu hình

### 2.1 `JwtOptions` (BuildingBlocks/Common)

File: `src/BuildingBlocks/Common/Auth/JwtOptions.cs`

```csharp
public sealed class JwtOptions
{
    public string Issuer    = "hdos-auth";   // iss claim
    public string Audience  = "hdos-api";    // aud claim
    public string Secret    = "";            // bắt buộc >= 32 ký tự
    public int ExpiresMinutes = 480;         // 8h
}
```

### 2.2 `appsettings.json` (mỗi service)

```json
"Jwt": {
  "Issuer": "hdos-auth",
  "Audience": "hdos-api",
  "Secret": "DEV-INSECURE-SECRET-CHANGE-ME-AT-LEAST-32-CHARS",
  "ExpiresMinutes": 480
}
```

### 2.3 docker-compose

Tất cả 5 services nhận `Jwt__Secret` từ env. Trên server, env này lấy từ `${JWT_SECRET}` trong `${ENV_DIR}/.env`:

```yaml
authservice:
  environment:
    Jwt__Secret: "${JWT_SECRET}"
    Jwt__Issuer: "hdos-auth"
    Jwt__Audience: "hdos-api"
```

> **Quan trọng**: `JWT_SECRET` phải GIỐNG NHAU giữa cả 5 services. AuthService sign bằng nó, các service khác verify cùng nó.

### 2.4 Phát token (`AuthService`)

`IJwtTokenIssuer.Issue(userId, email, fullName, roles, permissions)` (file `src/BuildingBlocks/Common/Auth/JwtTokenIssuer.cs`) sinh JWT chứa:

- `sub` = user.Id
- `email`, `name`, `preferred_username`
- `roles` — multi-value claim, mọi role của user (vd `admin`, `user`)
- `permission` — multi-value claim, flatten từ `RolePermissions → Permission.Key` (vd `orders:create`, `m01:read`)
- `jti`, `iss`, `aud`, `nbf`, `exp`

`LoginUserCommandHandler` load roles + permissions từ DB, truyền vào `Issue(...)`. Một token là snapshot quyền tại thời điểm login.

### 2.5 Verify token (`JwtAuthExtensions`)

File: `src/BuildingBlocks/Common/Auth/JwtAuthExtensions.cs`

```csharp
o.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer = true,           ValidIssuer       = opts.Issuer,
    ValidateAudience = true,         ValidAudience     = opts.Audience,
    ValidateLifetime = true,
    ValidateIssuerSigningKey = true, IssuerSigningKey  = SymmetricKey(opts.Secret),
    ClockSkew = 30s,
    RoleClaimType = "roles",
    NameClaimType = "preferred_username",
};
```

Special: SSE/SignalR token có thể đến qua `?access_token=<jwt>` (vì EventSource/WebSocket không gửi Authorization header) — `OnMessageReceived` event xử lý.

---

## 3. Endpoints AuthService

| Method | Path | Mô tả |
|--------|------|-------|
| `POST` | `/auth/register` | `{ email, password, fullName }` → tạo user mới. Idempotent: trả 400 nếu email đã có. |
| `POST` | `/auth/login` | `{ email, password }` → `{ userId, email, token }`. 401 nếu sai credential. |
| `GET`  | `/auth/users/{id}` | (admin) Lấy profile user. |
| `GET`  | `/auth/health` | Health check, không cần auth. |

Admin endpoints (`/auth/roles/...`, `/auth/permissions/...`, `/auth/user-roles/...`) yêu cầu role `admin`.

> Endpoint `/auth/validate` đã bị xoá ở refactor 2026-05-20 (nó chỉ tồn tại để phục vụ nginx auth_request).

---

## 4. Schema DB (`AuthDb`)

```
Users              Roles              Permissions
─────              ─────              ───────────
Id (Guid PK)       Id (Guid PK)       Id (Guid PK)
Email (UQ)         Name (UQ)          Resource
FullName           Description        Action
PasswordHash       …                  Description
LastSeenUtc                           …
CreatedAt
UpdatedAt

UserRoles (FK)               RolePermissions (FK)
─────────                    ───────────────
UserId  → Users              RoleId       → Roles
RoleId  → Roles              PermissionId → Permissions
```

`PasswordHash` được sinh bởi `Microsoft.AspNetCore.Identity.PasswordHasher<User>` (PBKDF2-SHA256, 100k iterations).

---

## 5. Seed dữ liệu

Khi `AuthService` khởi động lần đầu, `AuthDataSeeder` tự tạo (idempotent):

**Permissions**: tất cả constants trong `HdosPermissions.All` (`orders:create`, `orders:read`, ..., `users:manage`).

**Roles**:
- `admin` — gắn TẤT CẢ permission.
- `user` — chỉ gắn các permission có action `read`.

**Users**:

| Email | Password (mặc định) | Role |
|-------|--------------------|------|
| `admin@hdos.dev` | `Admin1234!` | admin |
| `testuser@hdos.dev` | `Test1234!` | user |

Override password qua env: `Seed__AdminPassword`, `Seed__TestUserPassword`.

> Trên production thật, hãy set password riêng qua env hoặc disable seeder.

---

## 6. Permission claims & policy

`HdosPermissions` (file `src/BuildingBlocks/Common/Auth/HdosPermissions.cs`) là source of truth:

```csharp
public const string OrdersCreate  = "orders:create";
public const string OrdersRead    = "orders:read";
// ...
public const string UsersManage   = "users:manage";
```

Mỗi endpoint protected:

```csharp
[HttpPost]
[Authorize(Policy = HdosPermissions.OrdersCreate)]
public IActionResult Create(...) { ... }
```

Policies đăng ký trong `AddHdosAuthorization()` đọc thẳng `permission` claim trong JWT — **không cần middleware** trung gian. Trước đây có `PermissionsMiddleware` đọc header `X-User-Permissions` từ nginx; đã bị xoá ở refactor 2026-05-20.

---

## 7. Bảo mật & Lưu ý vận hành

- **Secret rotation**: đổi `JWT_SECRET` → tất cả token đang dùng vô hiệu. Restart cả 5 services đồng thời, tránh trạng thái mixed.
- **TTL ngắn vs UX**: hiện tại `ExpiresMinutes=480` (8h) cho dev tiện. Production nên 30–60 phút + refresh token (chưa implement).
- **Permission revocation**: vì permission nằm trong JWT, gỡ role không có hiệu lực ngay. Chấp nhận TTL hoặc thêm revocation list (Redis).
- **Password policy**: tối thiểu 8 ký tự (validator FluentValidation). Cần phức tạp hơn → sửa `RegisterUserCommandValidator`.
- **HTTPS**: trong prod browser luôn nói chuyện qua nginx HTTPS (`8443`). Internal Docker network HTTP plain.

---

## 8. Lấy token để test API

### Qua Swagger UI

1. Mở `https://<host>:8443/auth/swagger` → vào `POST /auth/login`.
2. Body: `{ "email": "admin@hdos.dev", "password": "Admin1234!" }`.
3. Copy `data.token` trong response.
4. Bấm nút **Authorize** (góc phải trên) → paste token → Authorize → Close.
5. Mọi request sau đó tự kèm `Authorization: Bearer ...`.

### Qua curl

```bash
TOKEN=$(curl -sk https://localhost:8443/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@hdos.dev","password":"Admin1234!"}' \
  | jq -r '.data.token')

# Decode JWT để xem permission claims (debug):
echo "$TOKEN" | cut -d. -f2 | base64 -d 2>/dev/null | jq

curl -sk https://localhost:8443/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"customerId":"...","items":[...]}'
```

---

## 9. Migration từ Keycloak (đã hoàn tất)

Trước đây hệ thống dùng Keycloak: container riêng + Postgres + realm.json + audience mapper + Auth Code + PKCE. Đã refactor vì:

- Audience mapper không tự apply sau khi realm tồn tại → cần script patch.
- Setup phức tạp cho 1 use-case đơn giản (email/password login).
- Nginx phải proxy `/realms/*` để tránh Mixed Content.
- Browser cert handshake với Keycloak qua HTTPS gây Mixed Content khi FE chưa HTTPS.

Custom JWT đơn giản hơn nhiều: 1 secret shared, 1 endpoint login. Cần SSO/social login → quay lại Keycloak hoặc Auth0 khi đó.

---

## 10. Cross-reference

- [05 — Nginx Gateway](./05-nginx-gateway.md) — nginx giờ chỉ là reverse proxy.
- [13 — Thêm tính năng](./13-them-tinh-nang.md) — checklist khi thêm endpoint protected.
- [16 — HTTPS, SSL](./16-https-ssl.md) — self-signed cert, không còn proxy Keycloak.
