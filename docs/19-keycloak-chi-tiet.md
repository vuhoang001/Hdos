# 19 — Keycloak: Đặc tả chi tiết & Hướng dẫn sử dụng

---

## Mục lục

1. [Tổng quan](#1-tổng-quan)
2. [Kiến trúc xác thực đầu cuối](#2-kiến-trúc-xác-thực-đầu-cuối)
3. [Keycloak — Cài đặt & Khởi động](#3-keycloak--cài-đặt--khởi-động)
4. [Realm hdos — Cấu hình chi tiết](#4-realm-hdos--cấu-hình-chi-tiết)
5. [Clients](#5-clients)
6. [Users & Roles trong Keycloak](#6-users--roles-trong-keycloak)
7. [RBAC trong AuthService DB](#7-rbac-trong-authservice-db)
8. [Luồng xác thực từng bước](#8-luồng-xác-thực-từng-bước)
9. [JIT Provisioning](#9-jit-provisioning)
10. [Tích hợp Frontend](#10-tích-hợp-frontend)
11. [Admin UI — Hướng dẫn sử dụng](#11-admin-ui--hướng-dẫn-sử-dụng)
12. [Admin REST API (AuthService)](#12-admin-rest-api-authservice)
13. [Cấu hình theo môi trường](#13-cấu-hình-theo-môi-trường)
14. [Thêm permission / role mới](#14-thêm-permission--role-mới)
15. [Reset & Troubleshooting](#15-reset--troubleshooting)

---

## 1. Tổng quan

Hệ thống Hdos dùng **Keycloak 24.0** làm Identity Provider (IdP) duy nhất. Keycloak quản lý:

- **Authentication** — đăng nhập, cấp JWT access token
- **User federation** — danh sách user, credentials
- **Realm roles** — nhãn phân loại user (`admin`, `user`, ...)

AuthService **không** tự quản lý login/password. Nhiệm vụ của AuthService:

| Nhiệm vụ | Mô tả |
|----------|-------|
| Validate token | Xác minh chữ ký JWT qua JWKS endpoint của Keycloak |
| JIT Provision | Tạo user profile local lần đầu token hợp lệ xuất hiện |
| Resolve RBAC | Tra cứu roles + permissions từ DB riêng của AuthService |
| Emit X-headers | Ghi kết quả vào response headers để nginx forward |

---

## 2. Kiến trúc xác thực đầu cuối

```
┌─────────────┐    (1) Login          ┌─────────────────────┐
│   Browser   │ ─────────────────────▶│  Keycloak :8080     │
│  / Frontend │ ◀─────────────────────│  realm: hdos        │
└─────────────┘    JWT access_token   └─────────────────────┘
       │
       │  (2) API Request
       │  Authorization: Bearer <JWT>
       ▼
┌──────────────────┐
│   nginx :5000    │  API Gateway
│                  │
│  auth_request    │──────────────────────────────────┐
│  /_auth_validate │                                  │
└──────────────────┘                                  │
       │                                              ▼
       │                                ┌─────────────────────┐
       │  (3) Subrequest                │   AuthService       │
       │  GET /auth/validate            │   /auth/validate    │
       │  Authorization: Bearer <JWT>   │                     │
       │                                │ 1. JwtBearer valid? │
       │                                │    (JWKS check)     │
       │                                │ 2. JIT Provision    │
       │                                │ 3. Resolve RBAC     │
       │                                │ ─────────────────── │
       │  200 OK + X-User-* headers     │  Response headers:  │
       │◀───────────────────────────────│  X-User-Id          │
       │                                │  X-User-Email       │
       │                                │  X-User-Roles       │
       │                                │  X-User-Permissions │
       │                                └─────────────────────┘
       │
       │  (4) Proxy to upstream + forward X-User-* headers
       ▼
┌─────────────────┐
│  OrderService   │  (hoặc bất kỳ service nào)
│  /orders/...    │
│                 │
│ PermissionsMiddleware đọc X-User-Permissions
│ → thêm "permission" claim vào ClaimsIdentity
│                 │
│ [Authorize(Policy = "orders:create")]
└─────────────────┘
```

---

## 3. Keycloak — Cài đặt & Khởi động

### Yêu cầu

- Docker + Docker Compose v2
- File `keycloak/hdos-realm.json` (có sẵn trong repo)

### Khởi động (tự động import realm)

```bash
# Khởi động Keycloak cùng với postgres backend của nó
docker compose up -d postgres-keycloak keycloak

# Keycloak boot ~30-45 giây, kiểm tra:
docker logs hdos-keycloak 2>&1 | grep -E "import|Started|ERROR"
```

Khi volume `hdos-kcdata` chưa tồn tại (fresh start), Keycloak tự import realm `hdos` từ `./keycloak/hdos-realm.json`.

**Log thành công trông như sau:**

```
INFO  Importing from directory /opt/keycloak/bin/../data/import
INFO  Realm 'hdos' imported
INFO  Import finished successfully
INFO  Added user 'admin' to realm 'master'
INFO  Keycloak 24.0.5 on JVM started in 12.5s. Listening on: http://0.0.0.0:8080
```

### Thông tin truy cập sau khi khởi động

| | Giá trị |
|-|---------|
| Admin UI | `http://localhost:8080` (dev) hoặc `http://192.168.100.60:8080` (server) |
| Admin username | `admin` |
| Admin password | `Admin1234!` (default, override bằng `KC_ADMIN_PASSWORD` trong `.env`) |

### Biến môi trường Keycloak (docker-compose.yml)

```yaml
keycloak:
  environment:
    KC_DB: postgres
    KC_DB_URL: "jdbc:postgresql://postgres-keycloak:5432/keycloak"
    KC_DB_USERNAME: keycloak
    KC_DB_PASSWORD: "${KC_DB_PASSWORD:-keycloak_dev_pass}"
    KEYCLOAK_ADMIN: admin
    KEYCLOAK_ADMIN_PASSWORD: "${KC_ADMIN_PASSWORD:-Admin1234!}"
    KC_HOSTNAME_STRICT: "false"
    KC_HTTP_ENABLED: "true"
  command: start-dev --import-realm
  volumes:
    - ./keycloak:/opt/keycloak/data/import:ro
```

> **Lưu ý:** `KEYCLOAK_ADMIN` / `KEYCLOAK_ADMIN_PASSWORD` là tên đúng cho Keycloak 24.  
> Keycloak 26+ mới dùng `KC_BOOTSTRAP_ADMIN_USERNAME` / `KC_BOOTSTRAP_ADMIN_PASSWORD`.

---

## 4. Realm hdos — Cấu hình chi tiết

Realm `hdos` là không gian làm việc của toàn bộ ứng dụng. Tất cả users, clients, roles đều nằm trong realm này.

### Cài đặt realm

| Thuộc tính | Giá trị | Ý nghĩa |
|-----------|---------|---------|
| `sslRequired` | `none` | Dev không cần HTTPS |
| `accessTokenLifespan` | 300s (5 phút) | Thời gian sống của JWT |
| `ssoSessionMaxLifespan` | 36000s (10 giờ) | Session tối đa |
| `loginWithEmailAllowed` | `true` | Có thể login bằng email |
| `registrationAllowed` | `false` | Không tự đăng ký — admin tạo |

### Realm roles

| Role | Mô tả |
|------|-------|
| `admin` | Quản trị viên — có toàn quyền trong AuthService Admin API |
| `user` | Người dùng thường |

> Roles trong Keycloak là nhãn phân loại. Quyền chi tiết (permissions) được quản lý riêng trong AuthService DB.

---

## 5. Clients

### 5.1 hdos-backend (confidential)

Dùng bởi AuthService để validate audience claim trong JWT.

| Thuộc tính | Giá trị |
|-----------|---------|
| Client ID | `hdos-backend` |
| Client Secret | `hdos-backend-dev-secret` |
| Type | Confidential (server-to-server) |
| Flow | Standard Flow + Direct Access Grants |
| Redirect URIs | `*` (dev) |

**Protocol Mappers được cấu hình:**

**1. Audience Mapper** — thêm `aud: hdos-backend` vào JWT:
```json
{
  "name": "audience-hdos-backend",
  "protocolMapper": "oidc-audience-mapper",
  "config": {
    "included.client.audience": "hdos-backend",
    "access.token.claim": "true"
  }
}
```
AuthService validate `ValidateAudience = true` với `Audience = "hdos-backend"`.

**2. Realm Roles Mapper** — thêm `roles: ["admin", "user"]` vào JWT ở top-level:
```json
{
  "name": "realm-roles",
  "protocolMapper": "oidc-usermodel-realm-role-mapper",
  "config": {
    "claim.name": "roles",
    "access.token.claim": "true",
    "multivalued": "true"
  }
}
```
AuthService đọc claim này qua `RoleClaimType = "roles"`, cho phép `[Authorize(Roles = "admin")]` hoạt động.

**Lấy token (curl):**
```bash
TOKEN=$(curl -s -X POST http://localhost:8080/realms/hdos/protocol/openid-connect/token \
  -d grant_type=password \
  -d client_id=hdos-backend \
  -d client_secret=hdos-backend-dev-secret \
  -d username=admin \
  -d password=Admin1234! | jq -r .access_token)
```

---

### 5.2 hdos-frontend (public / PKCE)

Dùng bởi Single Page Application. Không có client secret — bảo mật bằng PKCE.

| Thuộc tính | Giá trị |
|-----------|---------|
| Client ID | `hdos-frontend` |
| Client Secret | **Không có** (public client) |
| Type | Public |
| Flow | Standard Flow (Authorization Code + PKCE) |
| PKCE | S256 (bắt buộc) |
| Redirect URIs | `*` (dev — production cần thu hẹp) |

**Protocol Mappers:** Có cùng Realm Roles Mapper như hdos-backend.

---

## 6. Users & Roles trong Keycloak

### Users có sẵn sau import

| Username | Email | Password | Roles |
|----------|-------|----------|-------|
| `admin` | `admin@hdos.dev` | `Admin1234!` | `admin`, `user` |
| `testuser` | `testuser@hdos.dev` | `Test1234!` | `user` |

### Tạo user mới qua Admin UI

1. Vào `http://localhost:8080` → chọn realm **hdos**
2. **Users** → **Create new user**
3. Điền `Username`, `Email`, `First name`, `Last name` → **Create**
4. Tab **Credentials** → **Set password** → nhập password → tắt `Temporary`
5. Tab **Role mapping** → **Assign role** → chọn `admin` hoặc `user`

### Tạo user qua kcadm.sh (CLI)

```bash
# Login vào admin CLI
docker exec hdos-keycloak /opt/keycloak/bin/kcadm.sh \
  config credentials \
  --server http://localhost:8080 \
  --realm master \
  --user admin \
  --password Admin1234!

# Tạo user
docker exec hdos-keycloak /opt/keycloak/bin/kcadm.sh create users \
  -r hdos \
  -s username=newuser \
  -s email=newuser@hdos.dev \
  -s enabled=true

# Đặt password
docker exec hdos-keycloak /opt/keycloak/bin/kcadm.sh set-password \
  -r hdos \
  --username newuser \
  --new-password Password123!

# Gán role
docker exec hdos-keycloak /opt/keycloak/bin/kcadm.sh add-roles \
  -r hdos \
  --uusername newuser \
  --rolename user
```

---

## 7. RBAC trong AuthService DB

Keycloak chỉ quản lý **realm roles** (`admin`, `user`). Phân quyền chi tiết được quản lý riêng trong **AuthService DB**.

### Data Model

```
Users
  │  id = Keycloak sub (Guid)
  │  email, fullName, lastSeenUtc
  │
  └──< UserRoles >── Roles ──< RolePermissions >── Permissions
                      │                               │
                    name                         resource:action
                 (e.g. "doctor")              (e.g. "orders:create")
```

### Permissions có sẵn

```csharp
// HdosPermissions.cs
"orders:create"        // Tạo đơn hàng
"orders:read"          // Xem đơn hàng
"orders:update"        // Cập nhật đơn hàng
"orders:delete"        // Xóa đơn hàng
"notifications:read"   // Xem thông báo
"notifications:send"   // Gửi thông báo
"m01:read"             // Đọc module M01
"m01:write"            // Ghi module M01
"async:submit"         // Submit async job
"users:manage"         // Quản lý users
"roles:manage"         // Quản lý roles
```

### Quan hệ Keycloak roles ↔ AuthService DB

Keycloak roles (`admin`, `user`) **không tự động** ánh xạ sang permissions. Bạn phải:

1. Tạo Role trong AuthService DB (ví dụ: `"doctor"`)
2. Gán permissions cho Role đó
3. Gán Role đó cho User (theo `userId = Keycloak sub`)

> **Tóm tắt:** Keycloak quản lý "ai là admin/user". AuthService DB quản lý "admin/user được làm gì cụ thể".

---

## 8. Luồng xác thực từng bước

### Bước 1 — Frontend lấy token từ Keycloak

```
Frontend → POST /realms/hdos/protocol/openid-connect/token
         ← JWT access_token (RS256, signed bằng Keycloak private key)
```

JWT payload mẫu:
```json
{
  "sub": "550e8400-e29b-41d4-a716-446655440000",
  "email": "admin@hdos.dev",
  "preferred_username": "admin",
  "roles": ["admin", "user"],
  "aud": "hdos-backend",
  "iss": "http://keycloak:8080/realms/hdos",
  "exp": 1778750960,
  "iat": 1778750660
}
```

### Bước 2 — Frontend gọi API qua nginx

```http
GET /orders/
Host: localhost:5000
Authorization: Bearer eyJhbGciOiJSUzI1NiIs...
```

### Bước 3 — nginx gọi auth_request

nginx intercept mọi request đến `/orders/`, `/notifications/`, `/m01/`, `/async/`:

```nginx
auth_request /_auth_validate;
```

Gửi subrequest đến `GET /auth/validate` với cùng `Authorization: Bearer` header.

### Bước 4 — AuthService validate và resolve

**JwtBearer middleware** (tự động):
1. Tải JWKS từ `http://keycloak:8080/realms/hdos/.well-known/openid-configuration`
2. Xác minh chữ ký JWT bằng public key của Keycloak
3. Kiểm tra `aud`, `iss`, `exp`

**ValidateAndResolveQueryHandler** (application code):
1. Parse `sub` claim → `userId` (Guid)
2. Tra cứu user trong AuthService DB
   - Không tồn tại → **JIT Provision**: tạo mới, publish `UserRegisteredIntegrationEvent`
   - Tồn tại → cập nhật `LastSeenUtc`
3. Tra cứu roles + permissions của user
4. Trả về `UserContextDto { Roles, Permissions }`

**AuthController.Validate** ghi headers vào response:
```
X-User-Id: 550e8400-e29b-41d4-a716-446655440000
X-User-Email: admin@hdos.dev
X-User-Roles: admin,user
X-User-Permissions: orders:create,orders:read,m01:read,...
```

### Bước 5 — nginx forward headers đến upstream

```nginx
auth_request_set $user_permissions $upstream_http_x_user_permissions;
proxy_set_header X-User-Permissions $user_permissions;
proxy_pass http://orderservice;
```

### Bước 6 — Service đọc permissions

**PermissionsMiddleware** (BuildingBlocks/Common):
```csharp
// Đọc header → thêm claim vào ClaimsIdentity
var permissions = header.Split(',');
foreach (var perm in permissions)
    identity.AddClaim(new Claim("permission", perm));
```

**Controller authorization:**
```csharp
[Authorize(Policy = HdosPermissions.OrdersCreate)]  // "orders:create"
[HttpPost]
public async Task<IActionResult> Create(...) { ... }
```

---

## 9. JIT Provisioning

Khi một user đăng nhập lần đầu (Keycloak token hợp lệ nhưng chưa có trong AuthService DB):

```csharp
// ValidateAndResolveQueryHandler.cs
if (user is null)
{
    user = User.Provision(userId, email, fullName);
    await users.AddAsync(user, ct);
    await uow.SaveChangesAsync(ct);

    // Notify other services
    await eventBus.PublishAsync(
        new UserRegisteredIntegrationEvent(user.Id, user.Email.Value, user.FullName), ct);
}
```

**Hệ quả:**
- User profile tự tạo không có roles/permissions nào trong AuthService DB
- User sẽ bị `403 Forbidden` nếu cố truy cập endpoint cần permission
- Admin cần dùng [Admin API](#12-admin-rest-api-authservice) để gán role cho user mới

---

## 10. Tích hợp Frontend

### 10.1 Keycloak-js (SPA)

```bash
npm install keycloak-js
```

```javascript
// src/auth/keycloak.js
import Keycloak from 'keycloak-js';

const kc = new Keycloak({
  url: 'http://192.168.100.60:8080',  // URL Keycloak (public)
  realm: 'hdos',
  clientId: 'hdos-frontend',
});

export default kc;
```

```javascript
// src/main.js (hoặc index.js)
import kc from './auth/keycloak';

await kc.init({
  onLoad: 'check-sso',          // Tự login nếu có session, không redirect nếu chưa
  // onLoad: 'login-required',  // Redirect ngay nếu chưa đăng nhập
  pkceMethod: 'S256',           // Bắt buộc vì client bật PKCE
});

if (kc.authenticated) {
  console.log('User:', kc.tokenParsed?.email);
}

// Tự động refresh token trước 60 giây hết hạn
setInterval(() => {
  kc.updateToken(60).catch(() => kc.logout());
}, 30_000);
```

```javascript
// Gọi API — luôn kèm Bearer token
async function apiCall(url, options = {}) {
  await kc.updateToken(30);  // đảm bảo token còn hạn
  return fetch(url, {
    ...options,
    headers: {
      ...options.headers,
      Authorization: `Bearer ${kc.token}`,
    },
  });
}

// Ví dụ
const orders = await apiCall('http://192.168.100.60:5000/orders/');
```

### 10.2 Các thông số cấu hình

| Thông số | Giá trị |
|---------|---------|
| Authority / Issuer | `http://192.168.100.60:8080/realms/hdos` |
| Client ID | `hdos-frontend` |
| Client Secret | *(không có — public client)* |
| Scopes | `openid profile email` |
| PKCE Method | `S256` |
| Token endpoint | `http://192.168.100.60:8080/realms/hdos/protocol/openid-connect/token` |
| Authorization endpoint | `http://192.168.100.60:8080/realms/hdos/protocol/openid-connect/auth` |
| JWKS URI | `http://192.168.100.60:8080/realms/hdos/protocol/openid-connect/certs` |
| Discovery | `http://192.168.100.60:8080/realms/hdos/.well-known/openid-configuration` |

### 10.3 Silent SSO (tùy chọn)

Tạo file `public/silent-check-sso.html`:
```html
<html>
<body>
<script>parent.postMessage(location.href, location.origin);</script>
</body>
</html>
```

Rồi khai báo trong init:
```javascript
await kc.init({
  onLoad: 'check-sso',
  pkceMethod: 'S256',
  silentCheckSsoRedirectUri: window.location.origin + '/silent-check-sso.html',
});
```

### 10.4 React + @react-keycloak/web

```bash
npm install @react-keycloak/web keycloak-js
```

```jsx
// src/auth/keycloak.js
import Keycloak from 'keycloak-js';
export const keycloak = new Keycloak({
  url: 'http://192.168.100.60:8080',
  realm: 'hdos',
  clientId: 'hdos-frontend',
});

// src/index.jsx
import { ReactKeycloakProvider } from '@react-keycloak/web';
import { keycloak } from './auth/keycloak';

root.render(
  <ReactKeycloakProvider
    authClient={keycloak}
    initOptions={{ onLoad: 'check-sso', pkceMethod: 'S256' }}
  >
    <App />
  </ReactKeycloakProvider>
);

// src/components/ProtectedRoute.jsx
import { useKeycloak } from '@react-keycloak/web';

export function ProtectedRoute({ children }) {
  const { keycloak, initialized } = useKeycloak();
  if (!initialized) return <div>Loading...</div>;
  if (!keycloak.authenticated) {
    keycloak.login();
    return null;
  }
  return children;
}
```

### 10.5 Lưu ý quan trọng về issuer

Token được lấy từ URL nào thì `iss` trong JWT sẽ khớp URL đó. AuthService validate `iss` theo `Keycloak__Authority`.

| Môi trường | Lấy token từ | `iss` trong JWT | AuthService Authority |
|-----------|-------------|-----------------|----------------------|
| Local dev (`dotnet run`) | `localhost:8080` | `http://localhost:8080/realms/hdos` | `http://localhost:8080/realms/hdos` |
| Docker Compose | `keycloak:8080` | `http://keycloak:8080/realms/hdos` | `http://keycloak:8080/realms/hdos` |
| Server (prod) | `192.168.100.60:8080` | `http://192.168.100.60:8080/realms/hdos` | `http://keycloak:8080/realms/hdos` ← **MISMATCH!** |

**Vấn đề production:** Frontend lấy token từ `192.168.100.60:8080`, nhưng AuthService trong Docker validate với `keycloak:8080`. Issuer không khớp → **401**.

**Giải pháp:** Đặt Keycloak sau nginx với một URL cố định, ví dụ `http://192.168.100.60:5000/auth-server/`. Hoặc cấu hình `KC_HOSTNAME` để Keycloak tự dùng URL public khi tạo token.

---

## 11. Admin UI — Hướng dẫn sử dụng

### Đăng nhập

```
URL: http://localhost:8080  (hoặc http://192.168.100.60:8080)
Username: admin
Password: Admin1234!
```

Sau khi login, chọn dropdown ở góc trên trái → chọn realm **hdos**.

### Quản lý Users

**Xem danh sách users:**
Sidebar → **Users** → danh sách tất cả users trong realm hdos.

**Tạo user mới:**
1. **Users** → **Create new user**
2. Điền thông tin → **Create**
3. Tab **Credentials** → **Set password** → nhập password → tắt `Temporary` → **Save**
4. Tab **Role mapping** → **Assign role** → chọn role

**Xem user ID (sub):**
Click vào user → URL có dạng `.../users/<UUID>` — UUID đó chính là `sub` claim trong JWT và là khóa chính trong AuthService DB.

### Quản lý Clients

Sidebar → **Clients**:
- `hdos-backend` — client cho backend services (confidential)
- `hdos-frontend` — client cho SPA (public, PKCE)

**Xem client secret (hdos-backend):**
Click `hdos-backend` → tab **Credentials** → **Client secret**.

**Kiểm tra mappers:**
Click client → tab **Client scopes** → click `hdos-backend-dedicated` → tab **Mappers**.
Phải có: `audience-hdos-backend` và `realm-roles`.

### Quản lý Realm Roles

Sidebar → **Realm roles**:
- Xem danh sách roles
- Tạo role mới: **Create role** → nhập name → **Save**

### Session Management

Sidebar → **Sessions** — xem active sessions, có thể revoke.

### Lấy token từ Admin UI (test)

1. Sidebar → **Realm settings** → tab **Tokens** — xem config token lifetime
2. Để lấy token test: dùng `curl` hoặc Postman với endpoint token (xem [Bước 1](#bước-1--frontend-lấy-token-từ-keycloak))

---

## 12. Admin REST API (AuthService)

Tất cả endpoint dưới đây yêu cầu:
- `Authorization: Bearer <token>` với role `admin` trong Keycloak
- Prefix: `/auth/admin/`

### Lấy token admin

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/realms/hdos/protocol/openid-connect/token \
  -d grant_type=password \
  -d client_id=hdos-backend \
  -d client_secret=hdos-backend-dev-secret \
  -d username=admin \
  -d password=Admin1234! | jq -r .access_token)
```

---

### Permissions API

#### Xem tất cả permissions

```bash
curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/auth/admin/permissions
```

Response:
```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "resource": "orders",
      "action": "create",
      "key": "orders:create",
      "description": "Tạo đơn hàng mới"
    }
  ]
}
```

#### Tạo permission mới

```bash
curl -X POST http://localhost:5000/auth/admin/permissions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "resource": "orders",
    "action": "create",
    "description": "Tạo đơn hàng mới"
  }'
```

#### Xóa permission

```bash
curl -X DELETE http://localhost:5000/auth/admin/permissions/{permissionId} \
  -H "Authorization: Bearer $TOKEN"
```

#### Gán permission cho role

```bash
curl -X POST http://localhost:5000/auth/admin/permissions/{roleId}/permissions/{permissionId} \
  -H "Authorization: Bearer $TOKEN"
```

#### Thu hồi permission từ role

```bash
curl -X DELETE http://localhost:5000/auth/admin/permissions/{roleId}/permissions/{permissionId} \
  -H "Authorization: Bearer $TOKEN"
```

---

### Roles API

#### Xem tất cả roles (kèm permissions)

```bash
curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/auth/admin/roles
```

Response:
```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "name": "doctor",
      "description": "Bác sĩ",
      "permissions": ["orders:create", "orders:read", "m01:read"]
    }
  ]
}
```

#### Tạo role mới

```bash
curl -X POST http://localhost:5000/auth/admin/roles \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "doctor",
    "description": "Bác sĩ điều trị"
  }'
```

#### Cập nhật role

```bash
curl -X PUT http://localhost:5000/auth/admin/roles/{roleId} \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "senior-doctor",
    "description": "Bác sĩ cao cấp"
  }'
```

#### Xóa role

```bash
curl -X DELETE http://localhost:5000/auth/admin/roles/{roleId} \
  -H "Authorization: Bearer $TOKEN"
```

---

### User Roles API

`userId` = Keycloak `sub` claim (Guid). Lấy từ JWT hoặc Admin UI.

#### Xem roles của user

```bash
curl -H "Authorization: Bearer $TOKEN" \
  http://localhost:5000/auth/admin/users/{userId}/roles
```

#### Gán role cho user

```bash
curl -X POST http://localhost:5000/auth/admin/users/{userId}/roles/{roleId} \
  -H "Authorization: Bearer $TOKEN"
```

#### Thu hồi role từ user

```bash
curl -X DELETE http://localhost:5000/auth/admin/users/{userId}/roles/{roleId} \
  -H "Authorization: Bearer $TOKEN"
```

---

### Workflow đầy đủ — Cấp quyền cho user mới

```bash
# 1. Lấy token admin
TOKEN=$(curl -s -X POST http://localhost:8080/realms/hdos/protocol/openid-connect/token \
  -d grant_type=password -d client_id=hdos-backend \
  -d client_secret=hdos-backend-dev-secret \
  -d username=admin -d password=Admin1234! | jq -r .access_token)

# 2. Tạo permissions (nếu chưa có)
P_CREATE=$(curl -s -X POST http://localhost:5000/auth/admin/permissions \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"resource":"orders","action":"create","description":"Tạo đơn hàng"}' \
  | jq -r .data.id)

P_READ=$(curl -s -X POST http://localhost:5000/auth/admin/permissions \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"resource":"orders","action":"read","description":"Xem đơn hàng"}' \
  | jq -r .data.id)

# 3. Tạo role
ROLE_ID=$(curl -s -X POST http://localhost:5000/auth/admin/roles \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"name":"doctor","description":"Bác sĩ"}' \
  | jq -r .data.id)

# 4. Gán permissions vào role
curl -X POST "http://localhost:5000/auth/admin/permissions/$ROLE_ID/permissions/$P_CREATE" \
  -H "Authorization: Bearer $TOKEN"
curl -X POST "http://localhost:5000/auth/admin/permissions/$ROLE_ID/permissions/$P_READ" \
  -H "Authorization: Bearer $TOKEN"

# 5. Gán role cho user (lấy userId từ JWT sub hoặc Keycloak Admin UI)
USER_ID="<keycloak-sub-uuid>"
curl -X POST "http://localhost:5000/auth/admin/users/$USER_ID/roles/$ROLE_ID" \
  -H "Authorization: Bearer $TOKEN"

echo "Done. User $USER_ID giờ có quyền orders:create, orders:read"
```

---

## 13. Cấu hình theo môi trường

### Local dev (dotnet run)

`appsettings.json`:
```json
{
  "Keycloak": {
    "Authority": "http://localhost:8080/realms/hdos",
    "Audience": "hdos-backend"
  }
}
```

### Docker Compose

`docker-compose.yml` (inject qua environment):
```yaml
environment:
  Keycloak__Authority: "http://keycloak:8080/realms/hdos"
  Keycloak__Audience: "hdos-backend"
```

Services trong Docker dùng hostname `keycloak` (Docker DNS tự resolve). Không dùng `localhost`.

### Server (Production)

`/opt/hdos-prod/common.env`:
```env
Keycloak__Authority=http://keycloak:8080/realms/hdos
Keycloak__Audience=hdos-backend
```

`docker-compose.server.yml` override password:
```yaml
keycloak:
  environment:
    KEYCLOAK_ADMIN_PASSWORD: "${KC_ADMIN_PASSWORD}"
```

`/opt/hdos-prod/.env`:
```env
KC_ADMIN_PASSWORD=<strong-password>
```

---

## 14. Thêm permission / role mới

### Bước 1 — Thêm constant (code)

```csharp
// BuildingBlocks/Common/Auth/HdosPermissions.cs
public const string ReportsView = "reports:view";
```

### Bước 2 — Đăng ký policy (code)

```csharp
// BuildingBlocks/Common/Auth/JwtAuthExtensions.cs — AddHdosAuthorization()
options.AddPolicy(HdosPermissions.ReportsView,
    p => p.RequireClaim("permission", HdosPermissions.ReportsView));
```

### Bước 3 — Dùng trên endpoint (code)

```csharp
[Authorize(Policy = HdosPermissions.ReportsView)]
[HttpGet("reports")]
public async Task<IActionResult> GetReports() { ... }
```

### Bước 4 — Tạo permission trong DB (runtime)

```bash
curl -X POST http://localhost:5000/auth/admin/permissions \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"resource":"reports","action":"view","description":"Xem báo cáo"}'
```

### Bước 5 — Gán cho role

```bash
curl -X POST "http://localhost:5000/auth/admin/permissions/$ROLE_ID/permissions/$PERM_ID" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

---

## 15. Reset & Troubleshooting

### Reset Keycloak về trạng thái ban đầu

```bash
cd ~/actions-runner/_work/Hdos/Hdos  # (server)
# hoặc cd <repo-root>                 # (local)

# Xóa container và volume, giữ nguyên data SQL Server
docker compose down keycloak postgres-keycloak
docker volume rm hdos_hdos-kcdata

# Restart — sẽ import lại realm từ keycloak/hdos-realm.json
docker compose up -d postgres-keycloak keycloak

# Chờ ~45s rồi kiểm tra
docker logs hdos-keycloak 2>&1 | grep -E "import|Started|admin"
```

### Test nhanh Keycloak

```bash
# Kiểm tra realm up
curl -s http://localhost:8080/realms/hdos/.well-known/openid-configuration | jq .issuer

# Lấy token (từ bên trong Docker network)
docker run --rm --network hdos_hdos-net alpine sh -c \
  "apk add -q curl jq && curl -s -X POST http://keycloak:8080/realms/hdos/protocol/openid-connect/token \
   -d grant_type=password -d client_id=hdos-backend \
   -d client_secret=hdos-backend-dev-secret \
   -d username=admin -d password=Admin1234! | jq '{iss:.iss, aud:.aud, roles:.roles, email:.email}' --raw-input --args"

# Test /auth/validate qua nginx
curl -v -H "Authorization: Bearer $TOKEN" http://localhost:5000/auth/validate
```

### Bảng Troubleshooting

| Triệu chứng | Nguyên nhân | Fix |
|------------|------------|-----|
| `401` mọi request | Keycloak chưa chạy | `docker compose up -d keycloak postgres-keycloak` |
| `401` — `user_not_found` | Admin user chưa được tạo | Dùng đúng env var `KEYCLOAK_ADMIN` (không phải `KC_BOOTSTRAP_ADMIN_*`) với Keycloak 24 |
| `401` — `iss` không khớp | Token từ `localhost:8080` nhưng service validate `keycloak:8080` | Lấy token từ đúng URL mà service thấy |
| `401` — `aud` sai | JWT không có `aud: hdos-backend` | Kiểm tra Audience mapper trong client `hdos-backend` |
| `401` — roles trống | JWT không có `roles` claim | Kiểm tra Realm Role mapper, Token Claim Name phải là `roles` |
| `403` dù token OK | User chưa có permissions trong AuthService DB | Gán permission qua Admin API |
| `client_not_found` | Client `hdos-frontend` chưa tồn tại | Reset Keycloak volume để re-import realm JSON mới |
| `/auth/validate` 500 | Migration chưa apply | Restart AuthService; check `docker logs hdos-authservice-1` |
| Realm không import | Volume `hdos-kcdata` đã tồn tại (IGNORE_EXISTING) | `docker volume rm hdos_hdos-kcdata` rồi restart |
| Admin UI không login | Password sai hoặc env var sai | Kiểm tra `KEYCLOAK_ADMIN_PASSWORD` trong docker inspect |
