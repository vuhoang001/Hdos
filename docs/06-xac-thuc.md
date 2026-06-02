# 06 — Xác thực, Phân quyền & License Management

Hệ thống Hdos dùng **custom JWT auth** do `AuthService` tự phát hành (HS256 với shared secret). Trước đây dùng Keycloak — đã bỏ vì over-engineered cho nhu cầu hiện tại.

**Mô hình hiện tại (kể từ refactor 2026-05-20):** mọi việc check JWT + permission đều ở **services**, nginx chỉ làm reverse proxy + TLS + CORS. Permissions nằm thẳng trong JWT claims — không còn `auth_request` ở nginx, không còn `X-User-*` headers, không còn `/auth/validate`.

---

## 1. Tổng quan luồng

```
┌─ Frontend / Swagger ──────────────────────────────────────┐
│ POST /auth/login { email, password }                       │
│   → nginx (dumb proxy) → authservice                       │
│   ← 200 OK { token: "eyJhbGc..." }                         │
│     JWT chứa: sub, email, roles, permission[], lic_*       │
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
│ ClaimsIdentity tự có claim "permission" và "lic_mod" từ JWT│
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

`IJwtTokenIssuer.Issue(userId, email, fullName, roles, permissions, licenseInfo?)` (file `src/BuildingBlocks/Common/Auth/JwtTokenIssuer.cs`) sinh JWT chứa:

- `sub` = user.Id
- `email`, `name`, `preferred_username`
- `roles` — multi-value claim, mọi role của user (vd `admin`, `user`)
- `permission` — multi-value claim, flatten từ `RolePermissions → Permission.Key` (vd `orders:create`, `m01:read`)
- `lic_plan`, `lic_mod[]`, `lic_exp` — nếu user có license active (xem [Section 10](#10-license-management))
- `jti`, `iss`, `aud`, `nbf`, `exp`

`LoginUserCommandHandler` load roles + permissions từ DB, query license, truyền vào `Issue(...)`. Một token là snapshot quyền + license tại thời điểm login.

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
| `GET`  | `/auth/admin/licenses/{userId}` | (admin) Lấy license active của user. |
| `POST` | `/auth/admin/licenses` | (admin) Gán hoặc thay thế license. |
| `DELETE` | `/auth/admin/licenses/{userId}` | (admin) Revoke license active. |

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

UserLicenses
────────────
Id (Guid PK)
UserId  → Users
Plan          (string — e.g. "pro", "enterprise")
ModulesCsv    (string — CSV của slugs: "orders,m01,forms")
ExpiresAtUtc  (DateTime? — null = vĩnh viễn)
IsActive      (bool)
CreatedAtUtc  (DateTime)
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

| Email | Password (mặc định) | Role | License |
|-------|--------------------|------|---------|
| `admin@hdos.dev` | `Admin1234!` | admin | `enterprise` — tất cả modules, vĩnh viễn |
| `testuser@hdos.dev` | `Test1234!` | user | `basic` — `orders`, `notifications`, +1 năm |

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

Policies đăng ký trong `AddHdosAuthorization()` đọc thẳng `permission` claim trong JWT — **không cần middleware** trung gian.

---

## 7. Bảo mật & Lưu ý vận hành

- **Secret rotation**: đổi `JWT_SECRET` → tất cả token đang dùng vô hiệu. Restart cả 5 services đồng thời, tránh trạng thái mixed.
- **TTL ngắn vs UX**: hiện tại `ExpiresMinutes=480` (8h) cho dev tiện. Production nên 30–60 phút + refresh token (chưa implement).
- **Permission revocation**: vì permission nằm trong JWT, gỡ role không có hiệu lực ngay. Chấp nhận TTL hoặc thêm revocation list (Redis).
- **License revocation**: tương tự — sau khi revoke, user cần login lại để JWT mới không có `lic_mod` claims.
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

# Decode JWT để xem tất cả claims (permission + license):
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

## 10. License Management

License Management cho phép kiểm soát **user nào được dùng module nào** trong hệ thống HDOS. Thay vì dựng thêm service riêng, license được **nhúng trực tiếp vào JWT** hiện có — không tốn thêm round-trip API, mọi service validate offline bằng token đang có sẵn.

```
Admin gán license → User login → JWT chứa license claims → Service đọc claim → Allow/Block
```

### 10.1 Kiến trúc

```
AuthService
├── Domain
│   └── Entities/UserLicense.cs          ← entity chính
│   └── Repositories/IUserLicenseRepository.cs
│
├── Infrastructure
│   └── Persistence/UserLicenseRepository.cs
│   └── Persistence/Configurations/UserLicenseConfiguration.cs
│   └── Persistence/AuthDbContextFactory.cs   ← design-time factory (migrations local)
│   └── Migrations/..._AddUserLicenses.cs
│
├── Application
│   ├── Features/License/AssignLicenseCommand.cs
│   ├── Features/License/RevokeLicenseCommand.cs
│   └── Features/License/GetUserLicenseQuery.cs
│   └── DTOs/LicenseDto.cs
│
└── API/Controllers/LicensesAdminController.cs  ← REST endpoints

BuildingBlocks/Common/Auth
├── LicenseClaimTypes.cs   ← claim name constants (lic_plan, lic_mod, lic_exp)
├── HdosModules.cs         ← module slug constants + HdosLicensePolicies
├── IJwtTokenIssuer.cs     ← thêm LicenseInfo? parameter
├── JwtTokenIssuer.cs      ← embed license claims vào JWT
└── JwtAuthExtensions.cs   ← đăng ký license policies
```

**Database:** bảng `UserLicenses` trong `AuthDb` (SQL Server).

### 10.2 Cách hoạt động

**Luồng gán license:**

```
Admin  POST /auth/admin/licenses
         │
         ▼
   AssignLicenseCommand
         │  revoke license cũ (nếu có)
         │  tạo UserLicense mới
         ▼
   AuthDb.UserLicenses
```

**Luồng login:**

```
User   POST /auth/login
         │
         ▼
   LoginUserCommandHandler
         │  1. verify password
         │  2. query roles + permissions  (như cũ)
         │  3. query UserLicenses WHERE UserId = ? AND IsActive = true
         │  4. nếu có license và chưa hết hạn → tạo LicenseInfo
         ▼
   JwtTokenIssuer.Issue(..., licenseInfo)
         │  embed lic_plan, lic_mod[], lic_exp vào JWT claims
         ▼
   JWT trả về client
```

**Luồng validate tại service:**

```
Client  GET /m01/something
  Authorization: Bearer <jwt>
         │
         ▼
   M01Service middleware
         │  verify JWT signature (offline, không cần gọi AuthService)
         │  đọc claim "lic_mod" → ["m01", "orders", ...]
         ▼
   [Authorize(Policy = HdosLicensePolicies.ModuleM01)]
         │  claim "lic_mod" chứa "m01" → 200 OK
         │  không có → 403 Forbidden
```

### 10.3 Plans & Modules

**Plans (gợi ý, không enforce cứng):**

| Plan | Ý nghĩa gợi ý |
|------|---------------|
| `free` | Dùng thử, giới hạn module |
| `basic` | Gói cơ bản |
| `pro` | Gói nâng cao |
| `enterprise` | Toàn quyền |

Plan chỉ là metadata — logic "plan X được dùng module nào" do admin quyết định khi gán license.

**Modules (constants trong `HdosModules`):**

| Constant | Slug | Service tương ứng |
|----------|------|-------------------|
| `HdosModules.Orders` | `orders` | OrderService |
| `HdosModules.Notifications` | `notifications` | NotificationService |
| `HdosModules.M01` | `m01` | M01Service |
| `HdosModules.DataMatching` | `data-matching` | DataMatchingService |
| `HdosModules.Forms` | `forms` | DynamicFormService |
| `HdosModules.Async` | `async` | AsyncGateway |

### 10.4 Cấu trúc JWT sau khi thêm license

```json
{
  "sub": "a1b2c3d4-...",
  "email": "doctor@hospital.vn",
  "name": "Bác sĩ A",
  "roles": ["user"],
  "permission": ["orders:read", "m01:read", "m01:write"],

  "lic_plan": "pro",
  "lic_mod":  "orders",
  "lic_mod":  "m01",
  "lic_mod":  "notifications",
  "lic_exp":  "2027-01-01T00:00:00.0000000Z",

  "iat": 1748822400,
  "exp": 1748851200
}
```

> `lic_mod` là multi-value claim — JWT chứa nhiều claim cùng tên, mỗi cái là 1 module.
>
> `lic_exp` là expiry của **license** (khác với `exp` là expiry của **token**).
> User có thể login sau ngày hết license nhưng `lic_mod` sẽ không được embed → service block.

### 10.5 API Reference

Base URL: `/auth/admin/licenses` — yêu cầu role `admin`.

#### `GET /auth/admin/licenses/{userId}`

Lấy license đang active của user.

**Response 200:**
```json
{
  "success": true,
  "data": {
    "id": "f1e2d3c4-...",
    "userId": "a1b2c3d4-...",
    "plan": "pro",
    "modules": ["orders", "m01", "notifications"],
    "expiresAtUtc": "2027-01-01T00:00:00Z",
    "isActive": true,
    "isExpired": false,
    "createdAtUtc": "2026-06-02T01:36:00Z"
  }
}
```

**Response 404** — user không có license active.

#### `POST /auth/admin/licenses`

Gán license cho user. Nếu đã có license active → tự động revoke và tạo mới.

**Request body:**
```json
{
  "userId": "a1b2c3d4-...",
  "plan": "pro",
  "modules": ["orders", "m01", "notifications", "forms"],
  "expiresAtUtc": "2027-01-01T00:00:00Z"
}
```

> `expiresAtUtc: null` → license vĩnh viễn, không encode `lic_exp` vào JWT.

**Response 200** — trả về `LicenseDto` của license vừa tạo.

#### `DELETE /auth/admin/licenses/{userId}`

Revoke license active của user.

**Response 200:** `{ "success": true }`

**Response 404** — user không có license active.

### 10.6 Hướng dẫn Admin (curl)

```bash
# Lấy token admin trước
TOKEN=$(curl -s -X POST https://localhost:8443/auth/login \
  -H "Content-Type: application/json" -k \
  -d '{"email":"admin@hdos.dev","password":"Admin1234!"}' \
  | jq -r '.data.token')

# Gán license pro cho user (1 năm)
curl -sk -X POST https://localhost:8443/auth/admin/licenses \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "USER_ID_ở_đây",
    "plan": "pro",
    "modules": ["orders", "m01", "notifications", "forms"],
    "expiresAtUtc": "2027-06-01T00:00:00Z"
  }'

# Xem license hiện tại
curl -sk https://localhost:8443/auth/admin/licenses/USER_ID_ở_đây \
  -H "Authorization: Bearer $TOKEN"

# Nâng cấp plan (chỉ POST lại — tự revoke cũ)
curl -sk -X POST https://localhost:8443/auth/admin/licenses \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "USER_ID_ở_đây",
    "plan": "enterprise",
    "modules": ["orders","m01","notifications","forms","data-matching","async"],
    "expiresAtUtc": null
  }'

# Revoke license
curl -sk -X DELETE https://localhost:8443/auth/admin/licenses/USER_ID_ở_đây \
  -H "Authorization: Bearer $TOKEN"
```

> Sau khi thay đổi license, user cần **login lại** để nhận JWT mới có claims cập nhật.

### 10.7 Bảo vệ endpoint bằng license

**Bước 1** — Đảm bảo service gọi `AddHdosAuthorization()` (đã có sẵn trong tất cả services).

**Bước 2** — Thêm `[Authorize]` vào controller/action:

```csharp
using Hdos.Common.Auth;

// Chỉ user có license module "m01" mới được vào
[Authorize(Policy = HdosLicensePolicies.ModuleM01)]
[HttpGet("patients")]
public async Task<IActionResult> GetPatients(...)

// Kết hợp permission + license
[Authorize(Policy = HdosPermissions.M01Read)]
[Authorize(Policy = HdosLicensePolicies.ModuleM01)]
[HttpGet("patients/{id}")]
public async Task<IActionResult> GetPatient(...)
```

**Bảng policy ↔ module:**

| Policy constant | Module slug |
|----------------|-------------|
| `HdosLicensePolicies.ModuleOrders` | `orders` |
| `HdosLicensePolicies.ModuleNotifications` | `notifications` |
| `HdosLicensePolicies.ModuleM01` | `m01` |
| `HdosLicensePolicies.ModuleDataMatching` | `data-matching` |
| `HdosLicensePolicies.ModuleForms` | `forms` |
| `HdosLicensePolicies.ModuleAsync` | `async` |

**Bước 3** — Đọc thông tin license trong code (tuỳ chọn):

```csharp
var plan    = User.FindFirstValue(LicenseClaimTypes.Plan);
var modules = User.FindAll(LicenseClaimTypes.Module).Select(c => c.Value).ToList();
var hasM01  = User.HasClaim(LicenseClaimTypes.Module, HdosModules.M01);
```

### 10.8 Thêm module mới

Ví dụ thêm module `billing`:

```csharp
// 1. HdosModules.cs — thêm constant
public const string Billing = "billing";
public static readonly IReadOnlyList<string> All = [
    Orders, Notifications, M01, DataMatching, Forms, Async, Billing,
];

// 2. HdosLicensePolicies (cùng file)
public const string ModuleBilling = "license:billing";

// 3. JwtAuthExtensions.cs — đăng ký policy
options.AddPolicy(HdosLicensePolicies.ModuleBilling,
    p => p.RequireClaim(LicenseClaimTypes.Module, HdosModules.Billing));

// 4. BillingService controller
[Authorize(Policy = HdosLicensePolicies.ModuleBilling)]
```

Không cần migration, không cần thay đổi DB — module slug chỉ là string trong `ModulesCsv`.

### 10.9 Tạo EF Migration khi không có SQL Server local

Dự án dùng `IDesignTimeDbContextFactory` tại `AuthService.Infrastructure/Persistence/AuthDbContextFactory.cs`. Factory cung cấp connection string giả — EF chỉ đọc schema, **không cần SQL Server thật**.

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

dotnet ef migrations add <TênMigration> \
  --project src/Services/AuthService/AuthService.Infrastructure \
  --startup-project src/Services/AuthService/AuthService.Infrastructure
```

Migration được apply tự động khi app khởi động trên server qua `MigrateAsync()` trong `Program.cs`.

---

## 11. Cross-reference

- [05 — Nginx Gateway & HTTPS](./05-nginx-gateway.md) — nginx là reverse proxy + TLS termination.
- [13 — Thêm tính năng](./13-them-tinh-nang.md) — checklist khi thêm endpoint protected.
