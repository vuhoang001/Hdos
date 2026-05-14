# 18 — Keycloak & RBAC

## Tổng quan

Hệ thống dùng **Keycloak** làm Identity Provider duy nhất. AuthService **không** xử lý login/register mà chỉ làm nhiệm vụ:

1. **Validate token** — nhận token Keycloak từ nginx `auth_request`, xác minh chữ ký qua JWKS
2. **JIT Provision** — tạo user profile local lần đầu token hợp lệ xuất hiện
3. **Resolve RBAC** — tra cứu roles + permissions của user từ DB riêng của AuthService
4. **Emit X-headers** — ghi `X-User-Permissions` vào response để nginx forward cho upstream services

```
Browser → Keycloak (login) → nhận JWT
Browser → nginx (request + Bearer token)
  nginx → AuthService /auth/validate (auth_request)
    AuthService: validate JWKS + resolve RBAC → X-User-Permissions
  nginx → forward request + X-User-Permissions → upstream Service
  Service: PermissionsMiddleware đọc X-User-Permissions → claims
  Service: [Authorize(Policy = "orders:create")] pass/fail
```

---

## Keycloak setup

### Cách 1 — Auto-import từ realm export (khuyến nghị)

Realm `hdos` được định nghĩa sẵn trong `keycloak/hdos-realm.json`. `docker-compose.yml` đã được cấu hình để **tự động import** khi Keycloak khởi động lần đầu:

```bash
# Chỉ cần start — realm được import tự động
docker compose up -d postgres-keycloak keycloak
```

Keycloak sẽ import realm khi **volume `hdos-kcdata` chưa tồn tại** (tức là lần đầu start hoặc sau khi xóa volume). Nếu realm đã tồn tại, import bị bỏ qua.

**Thông tin có sẵn sau import:**

| | Giá trị |
|--|---------|
| Admin UI | `http://localhost:8080` |
| Admin username | `admin` / `Admin1234!` |
| Realm | `hdos` |
| Client ID | `hdos-backend` |
| Client Secret | `hdos-backend-dev-secret` |
| Test users | `admin@hdos.dev` / `Admin1234!` (roles: admin, user) |
| | `testuser@hdos.dev` / `Test1234!` (role: user) |

**Reset Keycloak về trạng thái ban đầu:**

```bash
docker compose down
docker volume rm hdos-kcdata
docker compose up -d postgres-keycloak keycloak
# Keycloak sẽ import lại realm từ đầu
```

---

### Cách 2 — Setup thủ công qua Admin UI

Dùng khi cần tùy chỉnh hoặc thêm client mới (ví dụ `hdos-frontend`).

#### 1. Khởi động

```bash
docker compose up -d postgres-keycloak keycloak
```

#### 2. Tạo Realm `hdos`

Admin UI `http://localhost:8080` → **Create realm** → Name: `hdos`

#### 3. Tạo Client `hdos-backend`

1. **Clients** → **Create client**
2. Client ID: `hdos-backend`, Client authentication: **ON** (confidential)
3. Authentication flows: Standard flow + Direct access grants
4. **Valid redirect URIs**: `*`, **Web origins**: `*`
5. **Credentials tab** → ghi lại client secret

#### 4. Thêm Audience mapper (bắt buộc)

Để JWT chứa `aud: hdos-backend` (AuthService validate audience):

1. `hdos-backend` → **Client scopes** → `hdos-backend-dedicated`
2. **Add mapper** → **Audience**
3. Included Client Audience: `hdos-backend`, Add to access token: ON

#### 5. Thêm Roles mapper (bắt buộc)

Để JWT chứa `roles: ["admin", ...]` ở top-level (AuthService dùng `RoleClaimType = "roles"`):

1. `hdos-backend` → **Client scopes** → `hdos-backend-dedicated`
2. **Add mapper** → **User Realm Role**
3. Token Claim Name: `roles`, Add to ID token: ON, Add to access token: ON

#### 6. Tạo Realm roles

**Realm roles** → Create role: `admin`, `user`

#### 7. Tạo user

**Users** → Create → đặt email, Set password (Temporary: OFF) → **Role mapping** → assign roles

---

### Lấy token và test luồng đăng nhập

```bash
# Lấy access token
TOKEN=$(curl -s -X POST http://localhost:8080/realms/hdos/protocol/openid-connect/token \
  -d 'grant_type=password' \
  -d 'client_id=hdos-backend' \
  -d 'client_secret=hdos-backend-dev-secret' \
  -d 'username=admin' \
  -d 'password=Admin1234!' | jq -r .access_token)

# Kiểm tra claims trong token
echo $TOKEN | cut -d. -f2 | base64 -d 2>/dev/null | jq '{iss,aud,roles,email}'

# Test anonymous endpoint
curl http://localhost:5000/auth/health

# Test /auth/validate — trả về 200 + X-User-* headers
curl -v -H "Authorization: Bearer $TOKEN" http://localhost:5000/auth/validate

# Test protected endpoint
curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/orders/
```

**Lưu ý quan trọng về `iss` (issuer):**

Token phải được lấy qua URL mà Keycloak thấy từ bên trong — cùng URL với `Keycloak__Authority` của services. Trong Docker, services dùng `http://keycloak:8080`, nên:

| Ngữ cảnh | Token endpoint | `iss` trong JWT |
|---------|---------------|-----------------|
| Local dev (`dotnet run`) | `http://localhost:8080/...` | `http://localhost:8080/realms/hdos` |
| Docker compose | `http://keycloak:8080/...` | `http://keycloak:8080/realms/hdos` |

Nếu lấy token từ `localhost:8080` nhưng services validate với authority `keycloak:8080`, token sẽ bị từ chối 401 vì `iss` không khớp.

---

## RBAC Data Model

```
Roles ──< RolePermissions >── Permissions
  │
  └──< UserRoles >── (userId = Keycloak sub)
```

### Permissions
Một permission là cặp `resource:action`, ví dụ `orders:create`.

Định nghĩa sẵn trong `HdosPermissions.cs`:
```csharp
public static class HdosPermissions
{
    public const string OrdersCreate        = "orders:create";
    public const string OrdersRead          = "orders:read";
    public const string OrdersUpdate        = "orders:update";
    public const string OrdersDelete        = "orders:delete";
    public const string NotificationsRead   = "notifications:read";
    public const string NotificationsSend   = "notifications:send";
    public const string M01Read             = "m01:read";
    public const string M01Write            = "m01:write";
    public const string AsyncSubmit         = "async:submit";
    public const string UsersManage         = "users:manage";
    public const string RolesManage         = "roles:manage";
}
```

---

## Admin API (AuthService)

Tất cả endpoint admin yêu cầu `Authorization: Bearer <token>` với role `admin` trong Keycloak.

### Permissions

| Method | Path | Mô tả |
|--------|------|-------|
| `GET`  | `/auth/admin/permissions` | Liệt kê tất cả permissions |
| `POST` | `/auth/admin/permissions` | Tạo permission mới |
| `DELETE` | `/auth/admin/permissions/{id}` | Xóa permission |
| `POST` | `/auth/admin/permissions/{roleId}/permissions/{permissionId}` | Gán permission cho role |
| `DELETE` | `/auth/admin/permissions/{roleId}/permissions/{permissionId}` | Thu hồi permission từ role |

**Tạo permission:**
```http
POST /auth/admin/permissions
Authorization: Bearer <admin-token>

{
  "resource": "orders",
  "action": "create",
  "description": "Tạo đơn hàng mới"
}
```

### Roles

| Method | Path | Mô tả |
|--------|------|-------|
| `GET`    | `/auth/admin/roles` | Liệt kê roles (kèm permissions) |
| `POST`   | `/auth/admin/roles` | Tạo role mới |
| `PUT`    | `/auth/admin/roles/{id}` | Cập nhật role |
| `DELETE` | `/auth/admin/roles/{id}` | Xóa role |

**Tạo role:**
```http
POST /auth/admin/roles
Authorization: Bearer <admin-token>

{
  "name": "operator",
  "description": "Nhân viên vận hành"
}
```

### User Roles

| Method | Path | Mô tả |
|--------|------|-------|
| `GET`    | `/auth/admin/users/{userId}/roles` | Roles của user |
| `POST`   | `/auth/admin/users/{userId}/roles/{roleId}` | Gán role |
| `DELETE` | `/auth/admin/users/{userId}/roles/{roleId}` | Thu hồi role |

**`userId` = Keycloak `sub` claim (Guid)**

---

## Luồng validate và resolve permissions

```
nginx: auth_request → GET /auth/validate
  Headers: Authorization: Bearer <keycloak-jwt>
    │
    ▼
AuthService [Authorize] → JwtBearer middleware
  • Validate chữ ký JWT qua JWKS từ Keycloak:
    {Keycloak__Authority}/.well-known/openid-configuration
  • Nếu invalid → 401 (nginx trả 401 cho client)
    │
    ▼
ValidateAndResolveQueryHandler:
  1. Parse sub → userId (Guid)
  2. GetByIdAsync(userId) → User?
     Null → Provision(userId, email, fullName)
            → AddAsync + SaveChanges
            → Publish UserRegisteredIntegrationEvent
     Exists → UpdateLastSeen()
  3. GetRolesWithPermissionsAsync(userId)
  4. Collect permissions keys (e.g. "orders:create,m01:read")
    │
    ▼
AuthController.Validate:
  Response headers:
    X-User-Id: {guid}
    X-User-Email: {email}
    X-User-Roles: {role1,role2}
    X-User-Permissions: {perm1,perm2,...}
  Return 200 OK
    │
    ▼
nginx auth_request_set → proxy_set_header → upstream service
```

---

## Cách services sử dụng permissions

### PermissionsMiddleware

Đọc `X-User-Permissions` header → thêm vào `ClaimsIdentity` dưới dạng `claim["permission"]`:

```csharp
// WebApplicationExtensions.cs
app.UseAuthentication();
app.UseHdosPermissions();   // ← đọc X-User-Permissions header
app.UseAuthorization();
```

### Policy-based authorization

```csharp
// Program.cs của mỗi service
builder.Services.AddHdosAuthorization();
```

Đăng ký policies như:
```csharp
options.AddPolicy("orders:create",
    p => p.RequireClaim("permission", "orders:create"));
```

### Controller attribute

```csharp
[Authorize(Policy = HdosPermissions.OrdersCreate)]
[HttpPost]
public async Task<IActionResult> Create(...)
```

---

## Configuration

### appsettings.json

```json
{
  "Keycloak": {
    "Authority": "http://localhost:8080/realms/hdos",
    "Audience": "hdos-backend"
  }
}
```

### docker-compose.yml (Docker environment)

```yaml
Keycloak__Authority: "http://keycloak:8080/realms/hdos"
Keycloak__Audience: "hdos-backend"
```

**Lưu ý:** Trong Docker, services gọi `keycloak:8080` (tên container). Từ trình duyệt, dùng `localhost:8080`.

---

## Frontend (OIDC Authorization Code + PKCE)

Frontend tự implement Keycloak login — không qua AuthService:

```javascript
// keycloak.js
import Keycloak from 'keycloak-js';

const kc = new Keycloak({
  url: 'http://localhost:8080',
  realm: 'hdos',
  clientId: 'hdos-frontend',
});

await kc.init({ onLoad: 'check-sso', pkceMethod: 'S256' });

// Gọi API: đính token vào header
const response = await fetch('/orders/', {
  headers: { Authorization: `Bearer ${kc.token}` },
});
```

---

## Thêm permission mới

1. Thêm constant vào `HdosPermissions.cs`
2. Thêm policy vào `JwtAuthExtensions.AddHdosAuthorization()`
3. Gán `[Authorize(Policy = HdosPermissions.NewPerm)]` lên endpoint cần bảo vệ
4. Tạo permission trong DB qua Admin API
5. Gán permission cho role tương ứng

---

## Database Migration — InitRbac

Migration `20260514063243_InitRbac` thêm bảng RBAC (`Roles`, `Permissions`, `RolePermissions`, `UserRoles`) và cột `LastSeenUtc` vào `Users`.

Migration được viết với `IF NOT EXISTS` SQL để xử lý **cả hai trường hợp**:

- **Fresh install** (volume mới): Tạo bảng `Users` từ đầu + tạo RBAC tables.
- **Upgrade từ schema cũ** (`Init` migration đã apply): Chỉ thêm cột `LastSeenUtc`, xóa `PasswordHash` + `LastLoginUtc`, rồi tạo RBAC tables.

Migration tự chạy khi service khởi động (`context.Database.MigrateAsync()`). Không cần thao tác thủ công.

**Nếu migration bị stuck (server có DB cũ và migration fail):**

```bash
# 1. Xóa migration cũ khỏi history (nếu Init đã apply nhưng InitRbac chưa)
docker exec hdos-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P '<password>' -C -d AuthDb \
  -Q "DELETE FROM __EFMigrationsHistory WHERE MigrationId='20260508014638_Init'"

# 2. Restart AuthService — migration InitRbac sẽ tự apply
docker restart hdos-authservice-1

# 3. Kiểm tra
docker exec hdos-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P '<password>' -C -d AuthDb \
  -Q "SELECT MigrationId FROM __EFMigrationsHistory"
```

---

## Troubleshooting

| Triệu chứng | Nguyên nhân | Fix |
|------------|------------|-----|
| `401` mọi request | Keycloak chưa chạy hoặc realm `hdos` chưa tạo | `docker compose up keycloak`, hoặc xóa volume + restart để re-import |
| `401` dù token đúng | `Keycloak__Authority` sai URL hoặc `iss` không khớp | URL phải khớp từ phía service; trong Docker dùng `http://keycloak:8080/realms/hdos` |
| `401` dù iss đúng | `aud` trong JWT không phải `hdos-backend` | Thêm Audience mapper vào client `hdos-backend` |
| `/auth/validate` → `500` | Migration chưa apply (`LastSeenUtc` thiếu) | Xem mục "Database Migration" ở trên |
| `/auth/validate` → `500` | EF Include after Select (đã fix) | Cập nhật lên commit mới nhất |
| Email trong `X-User-Email` trống | ASP.NET Core map `email` → `ClaimTypes.Email` (đã fix) | Cập nhật lên commit mới nhất |
| JIT provision trả về `unknown@unknown.com` | `email` claim không được tìm thấy | Cập nhật lên commit mới nhất; xóa row lỗi: `DELETE FROM Users WHERE Email='unknown@unknown.com'` |
| `403` dù authenticated | User chưa có permission cần thiết trong AuthService DB | Gán permission cho role của user qua Admin API |
| `roles` claim trống trong JWT | Thiếu Realm Role mapper trong Keycloak client | Thêm User Realm Role mapper, Token Claim Name: `roles` |
| Realm không tự import | Volume `hdos-kcdata` đã tồn tại (Keycloak chỉ import lần đầu) | `docker volume rm hdos-kcdata` rồi start lại |
| JIT provision không chạy | `/auth/validate` bị skip | Kiểm tra nginx `auth_request /_auth_validate` |
