# 06 — Xác thực & Phân quyền

Hệ thống Hdos dùng **custom JWT auth** do `AuthService` tự phát hành (HS256 với shared secret). Trước đây dùng Keycloak — đã bỏ vì over-engineered cho nhu cầu hiện tại.

---

## 1. Tổng quan luồng

```
┌─ Frontend / Swagger ──────────────────────────────────────┐
│ POST /auth/login { email, password }                       │
│   → nginx → authservice                                    │
│   ← 200 OK { accessToken: "eyJhbGc..." }                   │
└────────────────────────────────────────────────────────────┘
                            │
                            ▼  (mọi request sau)
┌─ Browser ────────────────────────────────────────────────┐
│ POST /orders                                              │
│ Authorization: Bearer <accessToken>                       │
└────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─ nginx ──────────────────────────────────────────────────┐
│ 1. auth_request /_auth_validate (internal)                │
│    → GET authservice /auth/validate                       │
│      • JwtBearer middleware verify HS256 + iss/aud/exp    │
│      • Lookup user.roles + RolePermissions → headers      │
│      • 200 OK + X-User-Id, X-User-Roles, X-User-Permissions│
│ 2. Forward original POST /orders kèm các X-User-* headers │
└────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─ orderservice ───────────────────────────────────────────┐
│ JwtBearer middleware verify lần 2 (defense in depth)      │
│ PermissionsMiddleware: đọc X-User-Permissions → bơm vào   │
│   ClaimsIdentity thành permission claim                   │
│ [Authorize(Policy = "orders:create")] enforce             │
└────────────────────────────────────────────────────────────┘
```

**Defense in depth**: cả nginx (auth_request) và service đều verify JWT. Nếu bypass nginx và gọi service trực tiếp, vẫn cần valid Bearer token — chỉ thiếu permission claims (vì middleware chỉ điền khi có header từ nginx).

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

`IJwtTokenIssuer.Issue(userId, email, fullName, roles)` (file `src/BuildingBlocks/Common/Auth/JwtTokenIssuer.cs`) sinh JWT chứa:

- `sub` = user.Id
- `email`, `name`, `preferred_username`
- `roles` (multi-value claim — mọi role của user)
- `jti`, `iss`, `aud`, `nbf`, `exp`

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

Special: SSE/SignalR token có thể đến qua `?access_token=<jwt>` (vì WebSocket không gửi Authorization header) — `OnMessageReceived` event xử lý.

---

## 3. Endpoints AuthService

| Method | Path | Mô tả |
|--------|------|-------|
| `POST` | `/auth/register` | `{ email, password, fullName }` → tạo user mới. Idempotent: trả 400 nếu email đã có. |
| `POST` | `/auth/login` | `{ email, password }` → `{ userId, email, token }`. 401 nếu sai credential. |
| `GET`  | `/auth/validate` | (nội bộ) Gọi bởi nginx auth_request, verify JWT + ghi headers `X-User-*`. |
| `GET`  | `/auth/users/{id}` | (admin) Lấy profile user. |
| `GET`  | `/auth/health` | Health check, không cần auth. |

Admin endpoints (`/auth/roles/...`, `/auth/permissions/...`, `/auth/user-roles/...`) yêu cầu role `admin`.

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

Policies được đăng ký trong `AddHdosAuthorization()` — đọc `permission` claim từ ClaimsIdentity (do `PermissionsMiddleware` bơm vào từ `X-User-Permissions` header).

---

## 7. Bảo mật & Lưu ý vận hành

- **Secret rotation**: đổi `JWT_SECRET` → tất cả token đang dùng vô hiệu. Restart cả 5 services đồng thời, tránh trạng thái mixed.
- **TTL ngắn vs UX**: hiện tại `ExpiresMinutes=480` (8h) cho dev tiện. Production nên 30–60 phút + refresh token (chưa implement).
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
  | python3 -c 'import sys,json; print(json.load(sys.stdin)["data"]["token"])')

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

Custom JWT đơn giản hơn nhiều: 1 secret shared, 1 endpoint login, 1 endpoint validate. Cần SSO/social login → quay lại Keycloak hoặc Auth0 khi đó.

---

## 10. Cross-reference

- [05 — Nginx Gateway](./05-nginx-gateway.md) — `auth_request` flow, headers forwarding.
- [13 — Thêm tính năng](./13-them-tinh-nang.md) — checklist khi thêm endpoint protected.
- [16 — HTTPS, SSL](./16-https-ssl.md) — self-signed cert, không còn proxy Keycloak.
