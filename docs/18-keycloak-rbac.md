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

### 1. Khởi động

```bash
docker compose up keycloak postgres-keycloak
```

- Admin UI: `http://localhost:8080`
- Username/password: `admin` / `Admin1234!` (thay bằng `KC_ADMIN_PASSWORD` env)

### 2. Tạo Realm `hdos`

1. Admin UI → **Create realm** → Name: `hdos`

### 3. Tạo Client `hdos-backend`

1. **Clients** → **Create client**
2. Client ID: `hdos-backend`
3. Client authentication: **OFF** (public) — chỉ dùng để validate audience
4. **Valid redirect URIs**: `http://localhost:*`

### 4. Tạo Client `hdos-frontend`

1. **Clients** → **Create client**
2. Client ID: `hdos-frontend`
3. Client authentication: **OFF**
4. **Valid redirect URIs**: `http://localhost:5173/*`, `http://localhost:3000/*`
5. **Web origins**: `+`

### 5. Thêm audience mapper

Để JWT chứa `aud: hdos-backend`:

1. `hdos-frontend` → **Client scopes** → `hdos-frontend-dedicated`
2. **Add mapper** → **By configuration** → **Audience**
3. Name: `hdos-backend-aud`, Included Client Audience: `hdos-backend`

### 6. Thêm roles mapper

Để JWT chứa `roles: ["admin", ...]` ở top-level (AuthService dùng `RoleClaimType = "roles"`):

1. **Realm settings** → **User profile** — không cần thay đổi
2. `hdos-frontend` → **Client scopes** → `hdos-frontend-dedicated`
3. **Add mapper** → **By configuration** → **User Realm Role**
4. Name: `realm-roles`, Token Claim Name: `roles`, Add to ID token: ON, Add to access token: ON

### 7. Tạo Role `admin`

1. **Realm roles** → **Create role** → Name: `admin`
2. Assign cho admin user

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

## Troubleshooting

| Triệu chứng | Nguyên nhân | Fix |
|------------|------------|-----|
| `401` mọi request | Keycloak chưa chạy hoặc realm `hdos` chưa tạo | `docker compose up keycloak`, tạo realm |
| `401` dù token đúng | `Keycloak__Authority` sai URL | Đảm bảo URL khớp realm, ví dụ `.../realms/hdos` |
| `403` dù authenticated | User chưa có permission cần thiết | Gán permission cho role của user qua Admin API |
| Token `aud` không khớp | Thiếu audience mapper trong Keycloak | Thêm Audience mapper `hdos-backend` vào client scope |
| `roles` claim trống | Thiếu Realm Role mapper | Thêm User Realm Role mapper vào client scope |
| JIT provision không chạy | `/auth/validate` bị skip | Kiểm tra nginx `auth_request /_auth_validate` |
