# 14 — Bảo mật: đóng cổng nội bộ và JWT

Tài liệu này mô tả 2 lớp bảo mật đã được áp dụng để client bên ngoài chỉ có
**một đường vào duy nhất** (ApiGateway) và **bắt buộc phải đăng nhập** mới gọi
được các API nghiệp vụ.

> Phần phân quyền chi tiết (role, permission, policy theo nghiệp vụ) chưa làm
> trong vòng này — hiện chỉ kiểm tra "có token hợp lệ hay không". Sẽ phát triển
> tiếp sau.

## 1. Mô hình tổng thể

```
                         ┌──────────────────────────────┐
   client ──HTTP──►      │  ApiGateway (YARP)           │  ← chỉ port 5000 lộ ra
                         │  - Validate JWT              │
                         │  - Route theo path           │
                         └─────┬───────────┬───────────┘
                               │ internal  │
                               ▼           ▼
                         AuthService   OrderService   NotificationService
                         (validate JWT lại — defense in depth)
```

Hai lớp chồng nhau:

1. **Lớp 1 — Network**: chỉ ApiGateway có port public (`5000:8080`). 3 service
   nội bộ không map port host nữa, chỉ tồn tại trên Docker network `hdos-net`.
2. **Lớp 2 — JWT**: AuthService phát token sau khi login. Gateway và mỗi service
   tự validate token. Thiếu/sai token ⇒ 401.

## 2. Lớp 1 — Network

`docker-compose.yml`:

```yaml
authservice:
  # KHÔNG có block "ports:" → service chỉ reach được qua hdos-net
  networks: [hdos-net]

orderservice:
  # KHÔNG có block "ports:"
  networks: [hdos-net]

notificationservice:
  # KHÔNG có block "ports:"
  networks: [hdos-net]

apigateway:
  ports:
    - "5000:8080"      # cửa duy nhất ra ngoài
  networks: [hdos-net]
```

Hệ quả: gọi `curl http://localhost:5102/orders` từ máy host sẽ **fail** (không
có port mapping); muốn truy cập phải đi qua `http://localhost:5000/orders`.

Các service vẫn nói chuyện với nhau bình thường qua hostname Docker
(`http://authservice:8080`, `http://orderservice:8080`, ...).

> Khi chạy local **không** Docker (`dotnet run` 4 process), các service vẫn
> bind cổng `5101/5102/5103` trên `localhost`. Đây là môi trường dev, không
> phải production.

## 3. Lớp 2 — JWT

### 3.1 Cấu hình dùng chung

Section `Jwt` trong appsettings (hoặc env var dạng `Jwt__Secret`):

```json
"Jwt": {
  "Secret": "hdos-dev-secret-please-change-me-min-32-chars",
  "Issuer": "Hdos.Auth",
  "Audience": "Hdos.Services",
  "ExpiresMinutes": 60
}
```

Trong Docker compose, biến `JWT_SECRET` (env shell) sẽ override secret cho cả
4 service:

```bash
export JWT_SECRET="$(openssl rand -base64 48)"
docker compose up --build
```

### 3.2 Building block

File trong `src/BuildingBlocks/Common/Auth/`:

| File                     | Vai trò                                                            |
|--------------------------|--------------------------------------------------------------------|
| `JwtOptions.cs`          | POCO map từ section `Jwt`                                         |
| `IJwtTokenIssuer.cs`     | Hợp đồng phát token (chỉ AuthService dùng)                         |
| `JwtTokenIssuer.cs`      | Sinh JWT HS256 với claims `sub`, `email`, `jti`                    |
| `JwtAuthExtensions.cs`   | `AddHdosJwtAuth()` (validator) + `AddHdosJwtIssuer()` (issuer)     |

### 3.3 AuthService phát token

`LoginUserCommandHandler` inject `IJwtTokenIssuer`:

```csharp
var token = _tokenIssuer.Issue(user.Id, user.Email.Value);
return new LoginResultDto(user.Id, user.Email.Value, token.Token);
```

Token là JWT chuẩn HS256 ký bằng `Jwt:Secret`. Claims hiện có:

- `sub` = userId (Guid)
- `email` = email user
- `jti` = id duy nhất
- `iss`, `aud`, `nbf`, `exp` chuẩn

### 3.4 Gateway validate

`src/ApiGateway/Program.cs`:

```csharp
builder.Services.AddHdosJwtAuth(builder.Configuration);
...
app.UseAuthentication();
app.UseAuthorization();
app.MapReverseProxy();
```

Phân quyền theo route trong `appsettings.json`:

```json
"Routes": {
  "auth-route":          { "AuthorizationPolicy": "Anonymous", "Match": { "Path": "/auth/{**catch-all}" } },
  "orders-health-route": { "AuthorizationPolicy": "Anonymous", "Match": { "Path": "/orders/health" } },
  "orders-route":        { "AuthorizationPolicy": "Default",   "Match": { "Path": "/orders/{**catch-all}" } },
  "notifications-health-route": { "AuthorizationPolicy": "Anonymous", "Match": { "Path": "/notifications/health" } },
  "notifications-route": { "AuthorizationPolicy": "Default",   "Match": { "Path": "/notifications/{**catch-all}" } }
}
```

- `Anonymous` = không cần token
- `Default` = bắt buộc có token hợp lệ (chính sách mặc định của
  `AddAuthorization()`)

YARP match route cụ thể trước, nên `/orders/health` ăn route anonymous, các
path khác `/orders/...` ăn route Default.

### 3.5 Service validate (defense in depth)

Mỗi service cũng `AddHdosJwtAuth(...)` + `[Authorize]` ở controller, kèm
`[AllowAnonymous]` cho register/login/health:

```csharp
[ApiController]
[Route("auth")]
[Authorize]
public sealed class AuthController : ControllerBase
{
    [AllowAnonymous] [HttpPost("register")] ...
    [AllowAnonymous] [HttpPost("login")]    ...
                     [HttpGet("users/{id:guid}")] ...      // cần token
    [AllowAnonymous] [HttpGet("health")]    ...
}
```

Lý do làm cả gateway lẫn service: nếu sau này gateway bị bypass (rò rỉ vào
network nội bộ, deploy nhầm…), service vẫn từ chối request không token.

## 4. Luồng end-to-end

```
┌── client ───────────────────────────────────────────────────────────────┐
│                                                                         │
│  1. POST /auth/register   (anonymous OK)                                │
│  2. POST /auth/login      → 200 OK { token: "eyJhbGci..." }             │
│  3. POST /orders          Header: Authorization: Bearer eyJhbGci...     │
│         Gateway validate token  →  forward tới orderservice             │
│         orderservice validate token lại  →  xử lý                       │
│  4. GET  /notifications   Header: Authorization: Bearer eyJhbGci...     │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

Ví dụ curl đầy đủ:

```bash
# 1) Đăng ký
curl -X POST http://localhost:5000/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"alice@hdos.io","fullName":"Alice","password":"secret123"}'

# 2) Login → lấy token
TOKEN=$(curl -s -X POST http://localhost:5000/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"alice@hdos.io","password":"secret123"}' \
  | jq -r '.data.token')
echo "$TOKEN"

# 3) Tạo order (KHÔNG token sẽ ra 401)
curl -X POST http://localhost:5000/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "customerId":"00000000-0000-0000-0000-000000000001",
    "customerEmail":"alice@hdos.io",
    "items":[{"productName":"Book","quantity":2,"unitPrice":15.50,"currency":"USD"}]
  }'

# 4) Xem notifications
curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/notifications

# Health vẫn anonymous
curl http://localhost:5000/health
curl http://localhost:5000/orders/health
```

Test "đường vòng" để chắc Lớp 1:

```bash
# Sẽ fail "Connection refused" vì port không bind ra host nữa
curl -v http://localhost:5102/orders/health
```

## 5. Hướng phát triển tiếp

- Phân quyền chi tiết: thêm claim `roles`/`permissions` lúc issue token, dùng
  `[Authorize(Policy = "...")]` ở từng action.
- Refresh token + revoke (lưu jti vào DB hoặc Redis).
- Thay HS256 (symmetric) bằng RS256 (asymmetric) — service chỉ giữ public
  key, AuthService giữ private key, dễ rotate.
- Đưa secret ra Vault/Secret Manager thay vì env trong compose.
- Áp `[Authorize]` cho gRPC `UserGrpcService` nếu mở ra ngoài cluster.
