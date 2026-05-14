# 06 — Xác thực & Phân quyền

> Chi tiết đầy đủ về Keycloak setup, RBAC data model, Admin API và frontend integration xem tại **[docs/18-keycloak-rbac.md](./18-keycloak-rbac.md)**.

---

## Tổng quan: Defense in Depth

Hệ thống dùng **hai lớp** bảo vệ:

```
Browser
  │  1. Login → Keycloak (OIDC)  → nhận JWT (RS256)
  │  2. Gọi API + Bearer token
  ▼
nginx          ← Lớp 1: auth_request → AuthService /auth/validate
  │ (pass: 200, kèm X-User-Permissions header)
  ▼
Service        ← Lớp 2: [Authorize(Policy = "perm")] + PermissionsMiddleware
```

**Tại sao cần hai lớp?**
- nginx filter sớm mọi request không hợp lệ trước khi chạm service
- Service tự validate JWT (defense in depth nếu ai bypass nginx)
- X-User-Permissions chứa permissions đã resolve từ AuthService DB — service không cần biết về Keycloak roles

---

## Keycloak: Identity Provider

Keycloak quản lý **toàn bộ authentication**:
- Đăng nhập / đăng ký / quên mật khẩu qua UI Keycloak
- OIDC Authorization Code + PKCE cho frontend
- Phát hành JWT (RS256, validate bằng JWKS)
- Quản lý session, refresh token

```
Authority: http://keycloak:8080/realms/hdos  (Docker)
           http://localhost:8080/realms/hdos  (local dev)
Audience:  hdos-backend
```

---

## AuthService: RBAC & Validation

AuthService **không** xử lý login/register. Trách nhiệm duy nhất:

1. **Validate JWT** — dùng JWKS từ Keycloak (`Authority/.well-known/openid-configuration`)
2. **JIT Provision** — tạo user profile local lần đầu nhận token hợp lệ
3. **Resolve RBAC** — tra Roles → Permissions từ DB của AuthService
4. **Emit X-headers** — ghi `X-User-Permissions: perm1,perm2,...` để nginx forward

### RBAC hierarchy

```
Roles ──< RolePermissions >── Permissions (resource:action)
  └──< UserRoles >── User (keyed by Keycloak sub)
```

Endpoint admin (yêu cầu Keycloak role `admin`):
- `POST   /auth/admin/roles` — tạo role
- `POST   /auth/admin/permissions` — tạo permission  
- `POST   /auth/admin/roles/{roleId}/permissions/{permId}` — gán permission cho role
- `POST   /auth/admin/users/{userId}/roles/{roleId}` — gán role cho user

---

## Luồng gọi Protected API

```
GET /orders/
Authorization: Bearer <keycloak-jwt>
         │
         ▼
nginx: auth_request → /_auth_validate → AuthService /auth/validate
         │
    AuthService:
    • Validate JWKS chữ ký JWT
    • JIT provision user nếu chưa có
    • Query Roles + Permissions từ DB
    • Response 200 + headers:
        X-User-Id: {guid}
        X-User-Permissions: orders:read,orders:create,...
         │
         ▼
nginx: auth_request_set → proxy_set_header X-User-Permissions ...
nginx forward request + X-User-Permissions → OrderService
         │
         ▼
OrderService:
• JwtBearer validate token (lần 2, defense in depth)
• PermissionsMiddleware đọc X-User-Permissions → thêm claim("permission", "orders:read")
• [Authorize(Policy = HdosPermissions.OrdersRead)] → pass
         │
         ▼
Response 200
```

---

## Fine-grained Authorization

Mỗi action dùng permission policy cụ thể:

```csharp
// OrdersController
[Authorize(Policy = HdosPermissions.OrdersCreate)]
[HttpPost]
public async Task<IActionResult> Create(...)

[Authorize(Policy = HdosPermissions.OrdersRead)]
[HttpGet("{id:guid}")]
public async Task<IActionResult> GetById(...)
```

AuthService admin endpoints dùng Keycloak role trực tiếp:
```csharp
[Authorize(Roles = "admin")]
[Route("auth/admin/roles")]
public sealed class RolesAdminController : ControllerBase
```

---

## SignalR: Token qua Query String

WebSocket không cho phép gửi custom header trong browser. SignalR dùng query string:

```javascript
const connection = new HubConnectionBuilder()
    .withUrl("/notifications/hubs/notifications?access_token=" + keycloak.token)
    .build();
```

nginx **không** dùng `auth_request` cho `/notifications/hubs/` — WebSocket upgrade không tương thích. Service tự validate token qua `OnMessageReceived` trong `JwtAuthExtensions`.

---

## Troubleshooting 401/403

| Triệu chứng | Nguyên nhân | Fix |
|------------|------------|-----|
| `401` mọi request | Keycloak chưa chạy hoặc realm `hdos` chưa tạo | `docker compose up keycloak`, tạo realm |
| `401` dù token đúng | `Keycloak__Authority` sai URL | URL phải khớp realm, ví dụ `.../realms/hdos` |
| `403` dù authenticated | User chưa có permission cần thiết | Gán permission cho role qua Admin API |
| Token `aud` không khớp | Thiếu audience mapper | Thêm Audience mapper `hdos-backend` vào client scope |
| `403` AuthService admin | User không có role `admin` trong Keycloak | Gán realm role `admin` cho user |
