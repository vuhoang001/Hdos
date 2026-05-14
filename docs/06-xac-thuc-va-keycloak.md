# 06 — Xác thực, Phân quyền & Keycloak

---

## Mục lục

1. [Defense in Depth](#1-defense-in-depth)
2. [Luồng xác thực đầy đủ](#2-luồng-xác-thực-đầy-đủ)
3. [Keycloak — Cài đặt & Khởi động](#3-keycloak--cài-đặt--khởi-động)
4. [Realm hdos — Cấu hình chi tiết](#4-realm-hdos--cấu-hình-chi-tiết)
5. [Clients](#5-clients)
6. [Users & Roles trong Keycloak](#6-users--roles-trong-keycloak)
7. [RBAC trong AuthService DB](#7-rbac-trong-authservice-db)
8. [JIT Provisioning](#8-jit-provisioning)
9. [Tích hợp Frontend](#9-tích-hợp-frontend)
10. [Admin UI — Hướng dẫn sử dụng](#10-admin-ui--hướng-dẫn-sử-dụng)
11. [Admin REST API (AuthService)](#11-admin-rest-api-authservice)
12. [Cấu hình theo môi trường](#12-cấu-hình-theo-môi-trường)
13. [Thêm permission / role mới](#13-thêm-permission--role-mới)
14. [SignalR: Token qua Query String](#14-signalr-token-qua-query-string)
15. [Reset & Troubleshooting](#15-reset--troubleshooting)

---

## 1. Defense in Depth

Hệ thống dùng **hai lớp bảo vệ** độc lập:

```
Browser
  │  1. Login → Keycloak (OIDC) → nhận JWT (RS256)
  │  2. Gọi API + Bearer token
  ▼
nginx (port 5000)    ← Lớp 1: auth_request → AuthService /auth/validate
  │                           (filter sớm, trước khi chạm service)
  │  pass: 200 + X-User-Permissions header
  ▼
Service              ← Lớp 2: [Authorize(Policy = "perm")] + PermissionsMiddleware
                              (defense in depth nếu ai bypass nginx)
```

**Tại sao cần hai lớp?**

- nginx chặn sớm request không hợp lệ — service không phải xử lý traffic thừa
- `X-User-Permissions` chứa permissions đã resolve từ AuthService DB — service không cần biết về Keycloak roles
- Nếu ai bypass nginx (nội bộ, sai cấu hình), service vẫn tự validate JWT

---

## 2. Luồng xác thực đầy đủ

```
┌─────────────┐     (1) Login          ┌──────────────────────┐
│   Browser   │ ──────────────────────▶│  Keycloak :8080      │
│  / Frontend │ ◀──────────────────────│  realm: hdos         │
└─────────────┘     JWT access_token   └──────────────────────┘
       │
       │  (2) API Request
       │  Authorization: Bearer <JWT>
       ▼
┌──────────────────┐
│   nginx :5000    │
│                  │
│  auth_request    │──────────────────────────────────────┐
│  /_auth_validate │                                      │
└──────────────────┘                                      │
       │                                                  ▼
       │                                    ┌─────────────────────────┐
       │  (3) Subrequest                    │   AuthService           │
       │  GET /auth/validate                │   /auth/validate        │
       │  Authorization: Bearer <JWT>       │                         │
       │                                    │ 1. JwtBearer validate   │
       │                                    │    (JWKS check)         │
       │                                    │ 2. JIT Provision        │
       │                                    │ 3. Resolve RBAC         │
       │  200 OK + X-User-* headers         │                         │
       │◀───────────────────────────────────│  X-User-Id              │
       │                                    │  X-User-Email           │
       │                                    │  X-User-Roles           │
       │                                    │  X-User-Permissions     │
       │                                    └─────────────────────────┘
       │
       │  (4) Proxy to upstream + forward X-User-* headers
       ▼
┌──────────────────────┐
│  OrderService        │
│  /orders/...         │
│                      │
│ JwtBearer validate   │  ← lần 2 (defense in depth)
│ PermissionsMiddleware│  ← đọc X-User-Permissions → "permission" claim
│                      │
│ [Authorize(Policy =  │
│  "orders:create")]   │
└──────────────────────┘
```

### Chi tiết từng bước

**Bước 1 — Frontend lấy token từ Keycloak**
```bash
TOKEN=$(curl -s -X POST http://localhost:8080/realms/hdos/protocol/openid-connect/token \
  -d grant_type=password \
  -d client_id=hdos-backend \
  -d client_secret=hdos-backend-dev-secret \
  -d username=admin -d password=Admin1234! | jq -r .access_token)
```

JWT payload mẫu:
```json
{
  "sub": "550e8400-e29b-41d4-a716-446655440000",
  "email": "admin@hdos.dev",
  "preferred_username": "admin",
  "roles": ["admin", "user"],
  "aud": "hdos-backend",
  "iss": "http://keycloak:8080/realms/hdos"
}
```

**Bước 2 — AuthService validate và resolve**

`JwtBearer` middleware tự động:
- Tải JWKS từ `http://keycloak:8080/realms/hdos/.well-known/openid-configuration`
- Xác minh chữ ký JWT bằng public key Keycloak
- Kiểm tra `aud`, `iss`, `exp`

`ValidateAndResolveQueryHandler` (application code):
1. Parse `sub` claim → `userId` (Guid)
2. JIT Provision nếu user chưa tồn tại
3. Tra cứu roles + permissions của user từ DB
4. Ghi headers vào response:
```
X-User-Id: 550e8400-e29b-41d4-a716-446655440000
X-User-Email: admin@hdos.dev
X-User-Roles: admin,user
X-User-Permissions: orders:create,orders:read,m01:read,...
```

**Bước 3 — Service đọc permissions**

```csharp
// PermissionsMiddleware — chạy sau UseAuthentication(), trước UseAuthorization()
var header = context.Request.Headers["X-User-Permissions"].ToString();
foreach (var perm in header.Split(',', ...))
    identity.AddClaim(new Claim("permission", perm));
```

```csharp
// Controller
[Authorize(Policy = HdosPermissions.OrdersCreate)]  // "orders:create"
[HttpPost]
public async Task<IActionResult> Create(...) { }

// AuthService admin — dùng Keycloak role trực tiếp
[Authorize(Roles = "admin")]
[Route("auth/admin/roles")]
public sealed class RolesAdminController : ControllerBase { }
```

---

## 3. Keycloak — Cài đặt & Khởi động

### Yêu cầu

- Docker + Docker Compose v2
- File `keycloak/hdos-realm.json` (có sẵn trong repo)

### Khởi động (tự động import realm)

```bash
docker compose up -d postgres-keycloak keycloak

# Chờ ~40 giây, kiểm tra:
docker logs hdos-keycloak 2>&1 | grep -E "import|Started|admin"
```

Log thành công:
```
INFO  Importing from directory /opt/keycloak/bin/../data/import
INFO  Realm 'hdos' imported
INFO  Import finished successfully
INFO  Added user 'admin' to realm 'master'
INFO  Keycloak 24.0.5 on JVM started in 12.5s
```

### Thông tin truy cập

| | Giá trị |
|-|---------|
| Admin UI (local) | `http://localhost:8080` |
| Admin UI (server) | `http://192.168.100.60:8080` |
| Admin username | `admin` |
| Admin password | `Admin1234!` (override bằng `KC_ADMIN_PASSWORD` trong `.env`) |

### docker-compose.yml — cấu hình Keycloak

```yaml
keycloak:
  image: quay.io/keycloak/keycloak:24.0
  container_name: hdos-keycloak
  command: start-dev --import-realm
  environment:
    KC_DB: postgres
    KC_DB_URL: "jdbc:postgresql://postgres-keycloak:5432/keycloak"
    KC_DB_USERNAME: keycloak
    KC_DB_PASSWORD: "${KC_DB_PASSWORD:-keycloak_dev_pass}"
    KEYCLOAK_ADMIN: admin                                    # ← đúng cho Keycloak 24
    KEYCLOAK_ADMIN_PASSWORD: "${KC_ADMIN_PASSWORD:-Admin1234!}"
    KC_HOSTNAME_STRICT: "false"
    KC_HTTP_ENABLED: "true"
  volumes:
    - ./keycloak:/opt/keycloak/data/import:ro
```

> `KEYCLOAK_ADMIN` / `KEYCLOAK_ADMIN_PASSWORD` là tên đúng cho **Keycloak 24**.  
> Keycloak 26+ mới dùng `KC_BOOTSTRAP_ADMIN_USERNAME` / `KC_BOOTSTRAP_ADMIN_PASSWORD`.

---

## 4. Realm hdos — Cấu hình chi tiết

| Thuộc tính | Giá trị | Ý nghĩa |
|-----------|---------|---------|
| `sslRequired` | `none` | Dev không cần HTTPS |
| `accessTokenLifespan` | 300s | Thời gian sống JWT |
| `ssoSessionMaxLifespan` | 36000s | Session tối đa |
| `loginWithEmailAllowed` | `true` | Login bằng email |
| `registrationAllowed` | `false` | Admin tạo user, không tự đăng ký |

**Realm roles:**

| Role | Mô tả |
|------|-------|
| `admin` | Quản trị viên — toàn quyền AuthService Admin API |
| `user` | Người dùng thường |

> Roles Keycloak là nhãn phân loại. Quyền chi tiết (permissions) quản lý riêng trong AuthService DB.

---

## 5. Clients

### 5.1 hdos-backend (confidential)

Dùng bởi backend services để validate audience claim trong JWT.

| Thuộc tính | Giá trị |
|-----------|---------|
| Client ID | `hdos-backend` |
| Client Secret | `hdos-backend-dev-secret` |
| Type | Confidential |
| Flow | Standard Flow + Direct Access Grants |

**Protocol Mappers bắt buộc:**

**Audience Mapper** — thêm `aud: hdos-backend` vào JWT:
```json
{
  "name": "audience-hdos-backend",
  "protocolMapper": "oidc-audience-mapper",
  "config": { "included.client.audience": "hdos-backend", "access.token.claim": "true" }
}
```

**Realm Roles Mapper** — thêm `roles: ["admin", "user"]` vào JWT top-level:
```json
{
  "name": "realm-roles",
  "protocolMapper": "oidc-usermodel-realm-role-mapper",
  "config": { "claim.name": "roles", "access.token.claim": "true", "multivalued": "true" }
}
```
AuthService đọc claim này qua `RoleClaimType = "roles"` → `[Authorize(Roles = "admin")]` hoạt động.

---

### 5.2 hdos-frontend (public / PKCE)

Dùng bởi Single Page Application. Không có client secret — bảo mật bằng PKCE.

| Thuộc tính | Giá trị |
|-----------|---------|
| Client ID | `hdos-frontend` |
| Client Secret | **Không có** (public client) |
| Type | Public |
| Flow | Authorization Code + PKCE |
| PKCE | S256 (bắt buộc) |

---

## 6. Users & Roles trong Keycloak

### Users có sẵn sau import

| Username | Email | Password | Roles |
|----------|-------|----------|-------|
| `admin` | `admin@hdos.dev` | `Admin1234!` | `admin`, `user` |
| `testuser` | `testuser@hdos.dev` | `Test1234!` | `user` |

### Tạo user qua Admin UI

1. `http://localhost:8080` → chọn realm **hdos**
2. **Users** → **Create new user** → điền thông tin → **Create**
3. Tab **Credentials** → **Set password** → tắt `Temporary` → **Save**
4. Tab **Role mapping** → **Assign role** → chọn `admin` hoặc `user`

### Tạo user qua kcadm.sh (CLI)

```bash
# Login admin
docker exec hdos-keycloak /opt/keycloak/bin/kcadm.sh config credentials \
  --server http://localhost:8080 --realm master --user admin --password Admin1234!

# Tạo user
docker exec hdos-keycloak /opt/keycloak/bin/kcadm.sh create users \
  -r hdos -s username=newuser -s email=newuser@hdos.dev -s enabled=true

# Đặt password
docker exec hdos-keycloak /opt/keycloak/bin/kcadm.sh set-password \
  -r hdos --username newuser --new-password Password123!

# Gán role
docker exec hdos-keycloak /opt/keycloak/bin/kcadm.sh add-roles \
  -r hdos --uusername newuser --rolename user
```

> **Lấy userId:** Click vào user trong Admin UI → URL có dạng `.../users/<UUID>` — UUID đó là `sub` claim trong JWT, dùng làm khóa chính trong AuthService DB.

---

## 7. RBAC trong AuthService DB

Keycloak quản lý **realm roles** (`admin`, `user`). Phân quyền chi tiết được quản lý riêng trong **AuthService DB**.

### Data Model

```
Users (id = Keycloak sub)
  └──< UserRoles >── Roles ──< RolePermissions >── Permissions (resource:action)
```

### Permissions có sẵn

```csharp
// HdosPermissions.cs
"orders:create"        "orders:read"          "orders:update"     "orders:delete"
"notifications:read"   "notifications:send"
"m01:read"             "m01:write"
"async:submit"
"users:manage"         "roles:manage"
```

### Quan hệ Keycloak roles ↔ AuthService DB

Keycloak roles **không tự động** ánh xạ sang permissions. Admin phải:

1. Tạo Role trong AuthService DB (`"doctor"`)
2. Gán permissions cho Role đó
3. Gán Role đó cho User (theo `userId = Keycloak sub`)

---

## 8. JIT Provisioning

Khi user đăng nhập lần đầu (Keycloak token hợp lệ nhưng chưa có trong AuthService DB):

```csharp
// ValidateAndResolveQueryHandler.cs
if (user is null)
{
    user = User.Provision(userId, email, fullName);
    await users.AddAsync(user, ct);
    await uow.SaveChangesAsync(ct);

    await eventBus.PublishAsync(
        new UserRegisteredIntegrationEvent(user.Id, user.Email.Value, user.FullName), ct);
}
```

User profile tự tạo **không có roles/permissions** → sẽ bị `403` nếu cố truy cập endpoint cần permission.  
Admin cần dùng [Admin API](#11-admin-rest-api-authservice) để gán role.

---

## 9. Tích hợp Frontend

### Keycloak-js (SPA)

```bash
npm install keycloak-js
```

```javascript
// src/auth/keycloak.js
import Keycloak from 'keycloak-js';

export const kc = new Keycloak({
  url: 'http://192.168.100.60:8080',
  realm: 'hdos',
  clientId: 'hdos-frontend',
});
```

```javascript
// src/main.js
import { kc } from './auth/keycloak';

await kc.init({
  onLoad: 'check-sso',   // 'login-required' để redirect ngay
  pkceMethod: 'S256',
});

// Tự động refresh token
setInterval(() => kc.updateToken(60).catch(() => kc.logout()), 30_000);

// Gọi API
async function apiCall(url, options = {}) {
  await kc.updateToken(30);
  return fetch(url, {
    ...options,
    headers: { ...options.headers, Authorization: `Bearer ${kc.token}` },
  });
}
```

### React + @react-keycloak/web

```bash
npm install @react-keycloak/web keycloak-js
```

```jsx
// index.jsx
import { ReactKeycloakProvider } from '@react-keycloak/web';
import { kc } from './auth/keycloak';

root.render(
  <ReactKeycloakProvider authClient={kc}
    initOptions={{ onLoad: 'check-sso', pkceMethod: 'S256' }}>
    <App />
  </ReactKeycloakProvider>
);

// ProtectedRoute.jsx
import { useKeycloak } from '@react-keycloak/web';
export function ProtectedRoute({ children }) {
  const { keycloak, initialized } = useKeycloak();
  if (!initialized) return <div>Loading...</div>;
  if (!keycloak.authenticated) { keycloak.login(); return null; }
  return children;
}
```

### Thông số OIDC

| Thông số | Giá trị |
|---------|---------|
| Authority / Issuer | `http://192.168.100.60:8080/realms/hdos` |
| Client ID | `hdos-frontend` |
| Client Secret | *(không có — public)* |
| Scopes | `openid profile email` |
| PKCE | `S256` |
| Discovery | `http://192.168.100.60:8080/realms/hdos/.well-known/openid-configuration` |

### Lưu ý quan trọng về issuer

Token có `iss` khớp với URL đã dùng để lấy token. AuthService validate `iss` theo `Keycloak__Authority`.

| Môi trường | Lấy token từ | `iss` trong JWT | AuthService Authority |
|-----------|-------------|-----------------|----------------------|
| Local dev | `localhost:8080` | `http://localhost:8080/realms/hdos` | `http://localhost:8080/realms/hdos` |
| Docker | `keycloak:8080` | `http://keycloak:8080/realms/hdos` | `http://keycloak:8080/realms/hdos` |
| Server production | `192.168.100.60:8080` | `http://192.168.100.60:8080/realms/hdos` | `http://keycloak:8080/realms/hdos` ← **MISMATCH!** |

**Giải pháp production:** Đặt Keycloak sau nginx với URL cố định, hoặc cấu hình `KC_HOSTNAME` để Keycloak dùng URL public khi tạo token.

---

## 10. Admin UI — Hướng dẫn sử dụng

### Đăng nhập

```
URL: http://localhost:8080  (hoặc http://192.168.100.60:8080)
Username: admin
Password: Admin1234!
```

Sau khi login, dropdown góc trên trái → chọn realm **hdos**.

### Quản lý Users

**Sidebar → Users** → danh sách users.

**Tạo user:** Create new user → điền thông tin → Create → tab Credentials → Set password (tắt Temporary) → tab Role mapping → Assign role.

### Quản lý Clients

**Sidebar → Clients**: `hdos-backend` (confidential), `hdos-frontend` (public PKCE).

**Xem client secret (hdos-backend):** Click client → tab **Credentials**.

**Kiểm tra mappers:** Click client → tab **Client scopes** → `hdos-backend-dedicated` → tab **Mappers**.  
Phải có: `audience-hdos-backend` và `realm-roles`.

### Quản lý Realm Roles

**Sidebar → Realm roles**: tạo/xem/xóa roles.

### Sessions

**Sidebar → Sessions** — xem active sessions, revoke nếu cần.

---

## 11. Admin REST API (AuthService)

Tất cả endpoint yêu cầu `Authorization: Bearer <token>` với role `admin`.

### Lấy token admin

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/realms/hdos/protocol/openid-connect/token \
  -d grant_type=password -d client_id=hdos-backend \
  -d client_secret=hdos-backend-dev-secret \
  -d username=admin -d password=Admin1234! | jq -r .access_token)
```

### Permissions API — `/auth/admin/permissions`

```bash
# Xem tất cả
curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/auth/admin/permissions

# Tạo mới
curl -X POST http://localhost:5000/auth/admin/permissions \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"resource":"orders","action":"create","description":"Tạo đơn hàng"}'

# Xóa
curl -X DELETE http://localhost:5000/auth/admin/permissions/{id} \
  -H "Authorization: Bearer $TOKEN"

# Gán permission cho role
curl -X POST "http://localhost:5000/auth/admin/permissions/{roleId}/permissions/{permId}" \
  -H "Authorization: Bearer $TOKEN"

# Thu hồi permission
curl -X DELETE "http://localhost:5000/auth/admin/permissions/{roleId}/permissions/{permId}" \
  -H "Authorization: Bearer $TOKEN"
```

### Roles API — `/auth/admin/roles`

```bash
# Xem tất cả (kèm permissions)
curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/auth/admin/roles

# Tạo role
curl -X POST http://localhost:5000/auth/admin/roles \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"name":"doctor","description":"Bác sĩ điều trị"}'

# Cập nhật
curl -X PUT http://localhost:5000/auth/admin/roles/{id} \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"name":"senior-doctor","description":"Bác sĩ cao cấp"}'

# Xóa
curl -X DELETE http://localhost:5000/auth/admin/roles/{id} \
  -H "Authorization: Bearer $TOKEN"
```

### User Roles API — `/auth/admin/users/{userId}/roles`

`userId` = Keycloak `sub` claim (Guid).

```bash
# Xem roles của user
curl -H "Authorization: Bearer $TOKEN" \
  http://localhost:5000/auth/admin/users/{userId}/roles

# Gán role
curl -X POST "http://localhost:5000/auth/admin/users/{userId}/roles/{roleId}" \
  -H "Authorization: Bearer $TOKEN"

# Thu hồi role
curl -X DELETE "http://localhost:5000/auth/admin/users/{userId}/roles/{roleId}" \
  -H "Authorization: Bearer $TOKEN"
```

### Workflow đầy đủ — Cấp quyền cho user mới

```bash
# 1. Tạo permissions
P_CREATE=$(curl -s -X POST http://localhost:5000/auth/admin/permissions \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"resource":"orders","action":"create","description":"Tạo đơn hàng"}' \
  | jq -r .data.id)

# 2. Tạo role
ROLE_ID=$(curl -s -X POST http://localhost:5000/auth/admin/roles \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"name":"doctor","description":"Bác sĩ"}' | jq -r .data.id)

# 3. Gán permission vào role
curl -X POST "http://localhost:5000/auth/admin/permissions/$ROLE_ID/permissions/$P_CREATE" \
  -H "Authorization: Bearer $TOKEN"

# 4. Gán role cho user
curl -X POST "http://localhost:5000/auth/admin/users/$USER_ID/roles/$ROLE_ID" \
  -H "Authorization: Bearer $TOKEN"
```

---

## 12. Cấu hình theo môi trường

### Local dev (dotnet run)

```json
{
  "Keycloak": {
    "Authority": "http://localhost:8080/realms/hdos",
    "Audience": "hdos-backend"
  }
}
```

### Docker Compose

```yaml
environment:
  Keycloak__Authority: "http://keycloak:8080/realms/hdos"
  Keycloak__Audience: "hdos-backend"
```

Services trong Docker dùng hostname `keycloak` (Docker DNS). Không dùng `localhost`.

### Server (Production)

`/opt/hdos-prod/common.env`:
```env
Keycloak__Authority=http://keycloak:8080/realms/hdos
Keycloak__Audience=hdos-backend
KC_ADMIN_PASSWORD=<strong-password>
```

---

## 13. Thêm permission / role mới

**Bước 1 — Thêm constant (code):**
```csharp
// BuildingBlocks/Common/Auth/HdosPermissions.cs
public const string ReportsView = "reports:view";
```

**Bước 2 — Đăng ký policy (code):**
```csharp
// JwtAuthExtensions.cs — AddHdosAuthorization()
options.AddPolicy(HdosPermissions.ReportsView,
    p => p.RequireClaim("permission", HdosPermissions.ReportsView));
```

**Bước 3 — Dùng trên endpoint (code):**
```csharp
[Authorize(Policy = HdosPermissions.ReportsView)]
[HttpGet("reports")]
public async Task<IActionResult> GetReports() { }
```

**Bước 4 — Tạo permission trong DB (runtime):**
```bash
curl -X POST http://localhost:5000/auth/admin/permissions \
  -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" \
  -d '{"resource":"reports","action":"view","description":"Xem báo cáo"}'
```

**Bước 5 — Gán cho role:**
```bash
curl -X POST "http://localhost:5000/auth/admin/permissions/$ROLE_ID/permissions/$PERM_ID" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

---

## 14. SignalR: Token qua Query String

WebSocket không cho phép gửi custom header trong browser. SignalR dùng query string:

```javascript
const connection = new HubConnectionBuilder()
  .withUrl("/notifications/hubs/notifications?access_token=" + kc.token)
  .build();
```

nginx **không** dùng `auth_request` cho `/notifications/hubs/` — WebSocket upgrade không tương thích. Service tự validate token qua `OnMessageReceived` trong `JwtAuthExtensions`.

---

## 15. Reset & Troubleshooting

### Reset Keycloak về trạng thái ban đầu

```bash
cd ~/actions-runner/_work/Hdos/Hdos  # server
# hoặc cd <repo-root>                 # local

docker compose down keycloak postgres-keycloak
docker volume rm hdos_hdos-kcdata

# Restart — import lại từ keycloak/hdos-realm.json
docker compose up -d postgres-keycloak keycloak

# Chờ ~45s
docker logs hdos-keycloak 2>&1 | grep -E "import|Started|admin"
```

### Bảng lỗi thường gặp

| Triệu chứng | Nguyên nhân | Fix |
|------------|------------|-----|
| `401` mọi request | Keycloak chưa chạy | `docker compose up -d keycloak postgres-keycloak` |
| `401` — `user_not_found` | Admin user chưa tạo | Dùng `KEYCLOAK_ADMIN` (không phải `KC_BOOTSTRAP_ADMIN_*`) cho KC 24 |
| `401` — `iss` không khớp | Token từ `localhost` nhưng service validate `keycloak` | Lấy token từ đúng URL service thấy |
| `401` — `aud` sai | JWT thiếu `aud: hdos-backend` | Kiểm tra Audience mapper trong client `hdos-backend` |
| `401` — `roles` trống | JWT thiếu `roles` claim | Kiểm tra Realm Role mapper, Token Claim Name phải là `roles` |
| `403` dù token OK | User chưa có permissions trong AuthService DB | Gán permission qua Admin API |
| `client_not_found` | Client `hdos-frontend` chưa tồn tại | Reset Keycloak volume để re-import |
| `/auth/validate` 500 | Migration chưa apply | Restart AuthService; check logs |
| Realm không import | Volume `hdos-kcdata` đã tồn tại (IGNORE_EXISTING) | `docker volume rm hdos_hdos-kcdata` rồi restart |
| Admin UI không login | Env var sai hoặc password sai | Kiểm tra `KEYCLOAK_ADMIN_PASSWORD` trong docker inspect |
