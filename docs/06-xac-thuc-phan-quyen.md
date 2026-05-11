# 06 — Xác thực & Phân quyền

---

## Tổng quan: Defense in Depth

Hệ thống dùng **hai lớp** JWT validation:

```
Client
  │
  ▼
nginx          ← Lớp 1: auth_request → /auth/validate
  │ (nếu pass)
  ▼
Service        ← Lớp 2: [Authorize] attribute + JWT middleware
```

**Tại sao cần hai lớp?**
- Nếu chỉ có nginx: ai đó bypass nginx (VPN nội bộ, lỗi config) → service không có bảo vệ
- Nếu chỉ có service: mỗi service phải tự query AuthService để validate → coupling cao
- Cả hai: nginx filter phần lớn request không hợp lệ, service là lớp cuối cùng

---

## JWT Token

### Cấu trúc
```
Header.Payload.Signature
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.
eyJzdWIiOiJ7dXNlcklkfSIsImVtYWlsIjoiLi4uIiwianRpIjoiLi4uIiwibmJmIjoxNzAwMDAwMDAwLCJleHAiOjE3MDAwMDM2MDAsImlzcyI6Ikhkb3MuQXV0aCIsImF1ZCI6Ikhkb3MuU2VydmljZXMifQ.
{signature}
```

### Claims
| Claim | Giá trị | Mô tả |
|-------|---------|-------|
| `sub` | userId (Guid) | User ID |
| `email` | user@example.com | Email |
| `jti` | random Guid | JWT ID (unique per token) |
| `nbf` | Unix timestamp | Not Before — token chưa valid trước thời điểm này |
| `exp` | Unix timestamp | Expires — token hết hạn |
| `iss` | `Hdos.Auth` | Issuer |
| `aud` | `Hdos.Services` | Audience |

### Tạo token (AuthService only)
```csharp
// LoginUserCommandHandler.cs
var token = _tokenIssuer.Issue(user);
// JwtTokenIssuer tạo HS256 JWT với secret key từ config
```

### Config JWT
```json
// appsettings.json
{
  "Jwt": {
    "Secret": "BuSehDqHnOAoGDzmxgIlSPmTtSARXpsVN+/VUcTGC45LkuohRmgU0E6BlXpnqfEP",
    "Issuer": "Hdos.Auth",
    "Audience": "Hdos.Services",
    "ExpiresMinutes": 60
  }
}
```

**Quan trọng:** Secret phải giống nhau ở tất cả services (để validate). Trên server, inject qua environment variable `/opt/hdos-prod/common.env`.

---

## Luồng Login

```
POST /auth/login
Body: { "email": "user@example.com", "password": "Pass123!" }
         │
         ▼
[nginx location /auth/] → proxy thẳng, không auth_request
         │
         ▼
AuthService: LoginUserCommandHandler
  1. FindByEmail(email)
  2. VerifyPassword(password, user.PasswordHash)    ← BCrypt compare
  3. Nếu fail → Result.Failure("Invalid credentials")
  4. Nếu pass → jwtTokenIssuer.Issue(user)
  5. Raise UserLoggedInDomainEvent
         │
         ▼
Response 200:
{
  "success": true,
  "data": {
    "userId": "...",
    "email": "user@example.com",
    "token": "eyJhbGci..."
  }
}
```

---

## Luồng gọi Protected API

```
GET /m01/dashboard/summary
Headers:
  Authorization: Bearer eyJhbGci...
  Content-Type: application/json
         │
         ▼
nginx: location /m01/
  ├── auth_request → GET /_auth_validate (internal)
  │      │
  │      ▼
  │   nginx gửi subrequest → AuthService /auth/validate
  │      Headers: Authorization: Bearer eyJhbGci... (forward từ request gốc)
  │      │
  │      ▼
  │   AuthService: ValidateToken()
  │      • JWT middleware parse token
  │      • Kiểm tra: chữ ký, issuer, audience, expiry
  │      • Nếu valid → [Authorize] pass → 200 OK
  │      • Nếu invalid → 401 Unauthorized
  │      │
  │   ┌──┴──┐
  │   200   401
  │   │     └── nginx error_page 401 = @unauthorized
  │   │         return 401 '{"error":"Unauthorized",...}'
  │   │
  ▼   ▼ (tiếp theo nếu 200)
nginx forward request gốc → M01Service
  Headers: Authorization: Bearer eyJhbGci... (vẫn giữ)
         │
         ▼
M01Service: JWT middleware validate lại token (lần 2)
  → [Authorize] pass → Controller action chạy
         │
         ▼
Response 200: { "tongLuotKham": 128, ... }
```

---

## Authorization trong Controller

```csharp
[ApiController]
[Route("m01")]
[Authorize]                    // Tất cả action trong controller yêu cầu JWT
public class M01Controller : ControllerBase
{
    [HttpGet("dashboard/summary")]
    public async Task<IActionResult> GetSummary() { ... }

    [HttpGet("health")]
    [AllowAnonymous]           // Override class-level [Authorize]
    public IActionResult Health() => Ok();
}
```

---

## SignalR: Token qua Query String

WebSocket không cho phép gửi custom header trong browser. SignalR dùng query string:

```javascript
// Client
const connection = new HubConnectionBuilder()
    .withUrl("http://server:5000/notifications/hubs/notifications?access_token=" + token)
    .build();
```

```csharp
// Trong JwtAuthExtensions.cs (server)
o.Events = new JwtBearerEvents
{
    OnMessageReceived = ctx =>
    {
        var accessToken = ctx.Request.Query["access_token"];
        var path = ctx.HttpContext.Request.Path;
        if (!string.IsNullOrEmpty(accessToken) && path.Value!.Contains("/hubs/"))
            ctx.Token = accessToken;
        return Task.CompletedTask;
    }
};
```

---

## Troubleshooting 401/403

| Triệu chứng | Nguyên nhân | Fix |
|------------|------------|-----|
| `401` mọi request có JWT | `/auth/validate` endpoint thiếu hoặc 404 | Kiểm tra authservice đã deploy chưa |
| `500` thay vì `401` | `/auth/validate` trả code khác 2xx/401/403 | Check authservice logs |
| `401` dù token đúng | Sai `Jwt__Secret` ở một service | Đảm bảo tất cả services dùng cùng secret |
| `401` token hết hạn | Token quá 60 phút | Client cần refresh token / login lại |
| CORS error + 401 | CORS preflight bị từ chối trước | Kiểm tra `Access-Control-Allow-Headers` trong nginx |
| 401 chỉ với header custom | Header không có trong `Access-Control-Allow-Headers` | Thêm header vào nginx config hoặc dùng `*` |
