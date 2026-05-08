# 16 — Luồng request end-to-end và cách auth/validate ở từng tầng

Tài liệu này đi sâu vào câu hỏi: **một HTTP request đi từ client vào hệ thống
sẽ đi qua những tầng nào, ở mỗi tầng làm gì với JWT, và khi nào bị reject?**

Bổ trợ cho:

- [09 — API Gateway](./09-api-gateway.md) — chi tiết YARP routing.
- [14 — Bảo mật JWT](./14-bao-mat-jwt.md) — security model 2 lớp (network + JWT).
- [15 — SignalR](./15-signalr.md) — realtime hub.

## 1. Sơ đồ tổng

```
                  Authorization: Bearer <jwt>
   client ───────────────────────────────────────►  ApiGateway :5000
                                                        │
   ┌────────────────── pipeline gateway ────────────────┤
   │ 1. ExceptionHandlingMiddleware                     │
   │ 2. RequestLoggingMiddleware                        │
   │ 3. UseWebSockets                                   │
   │ 4. UseCors                                         │
   │ 5. UseAuthentication  ← parse + verify JWT         │
   │ 6. UseAuthorization   ← áp policy của route        │
   │ 7. UseSwaggerUI (gộp)                              │
   │ 8. MapReverseProxy    ← YARP forward               │
   └────────────────────────────────────────────────────┘
                                                        │  giữ nguyên header
                                                        ▼
                                              Service con :510x
   ┌────────────────── pipeline service ────────────────┐
   │ 1. UseSwagger / UseSwaggerUI (Dev)                 │
   │ 2. ExceptionHandlingMiddleware                     │
   │ 3. RequestLoggingMiddleware                        │
   │ 4. UseCors                                         │
   │ 5. UseAuthentication  ← validate JWT lần 2         │
   │ 6. UseAuthorization                                │
   │ 7. MapControllers     ← [Authorize] / [AllowAnonymous] │
   └────────────────────────────────────────────────────┘
                                                        ▼
                                              Controller → MediatR → Domain
```

Điểm cốt lõi: **mọi service share cùng 1 `Jwt` config (Secret + Issuer +
Audience)**. Vì JWT ký bằng HMAC-SHA256 (symmetric), service nào có secret là
verify được token offline — không phải gọi lại AuthService.

## 2. Pipeline order — vì sao thứ tự lại quan trọng

### 2.1 Gateway

`src/ApiGateway/Program.cs:14-49`:

```csharp
var app = builder.Build();

app.UseHdosMiddleware();   // ExceptionHandling → RequestLogging
app.UseWebSockets();       // bắt buộc đứng trước Auth để upgrade chạy được
app.UseHdosCors();

app.UseAuthentication();   // (5) parse Bearer token, gắn HttpContext.User
app.UseAuthorization();    // (6) áp AuthorizationPolicy của route

app.UseSwaggerUI(...);     // UI gộp swagger 4 service

app.MapGet("/", ...).AllowAnonymous();
app.MapGet("/health", ...).AllowAnonymous();

app.MapReverseProxy()      // (8) YARP — chỉ gọi khi đã pass authorization
   .RequireCors(HdosCorsPolicy);
```

Quy tắc: **Authentication phải đứng trước Authorization, và cả 2 phải đứng
trước MapReverseProxy**. Đảo thứ tự ⇒ YARP forward request trước khi check
token ⇒ service con vẫn nhận được request "lậu" (nhưng nó vẫn từ chối, xem 2.2
— đây là defense-in-depth).

### 2.2 Service con

Ví dụ `src/Services/OrderService/OrderService.API/Program.cs:38-42`:

```csharp
app.UseHdosMiddleware();
app.UseHdosCors();
app.UseAuthentication();   // validate JWT lần 2
app.UseAuthorization();
app.MapControllers();      // [Authorize] / [AllowAnonymous] kích hoạt ở đây
```

3 service (Auth/Order/Notification/M01) đều có pipeline giống hệt — đều gọi
`AddHdosJwtAuth(builder.Configuration)` với cùng section `Jwt`.

## 3. Trace từng bước — `POST /orders` thật

### Bước 0 — Lấy token

```bash
TOKEN=$(curl -s -X POST http://localhost:5000/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"alice@hdos.io","password":"secret123"}' \
  | jq -r '.data.token')
```

Đường đi của login:

1. Gateway match route `auth-route` (`appsettings.json:17-21`) với
   `AuthorizationPolicy: "Anonymous"` ⇒ skip kiểm token.
2. YARP forward sang `http://localhost:5101/auth/login` (giữ nguyên path).
3. AuthService — controller `AuthController.cs:31-39`, action `Login` đánh
   `[AllowAnonymous]`.
4. Handler `LoginUserCommandHandler` verify password rồi gọi
   `IJwtTokenIssuer.Issue(userId, email)` (`JwtTokenIssuer.cs:15-37`):
   - Ký HS256 bằng `Jwt:Secret`.
   - Claims: `sub = userId`, `email`, `jti = Guid`, kèm `iss`, `aud`, `nbf`, `exp`.

Kết quả: chuỗi JWT 3 phần `<header>.<payload>.<signature>`.

### Bước 1 — Client gửi request

```bash
curl -X POST http://localhost:5000/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "customerId":"...", "items":[...] }'
```

### Bước 2 — Tại Gateway

**(a) Authentication** — `JwtAuthExtensions.cs:24-41` đã đăng ký bearer scheme:

```csharp
o.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer = true,                        // iss == "Hdos.Auth"?
    ValidateAudience = true,                      // aud == "Hdos.Services"?
    ValidateLifetime = true,                      // còn hạn không (ClockSkew 30s)
    ValidateIssuerSigningKey = true,              // chữ ký HS256 đúng secret?
    ValidIssuer   = options.Issuer,
    ValidAudience = options.Audience,
    IssuerSigningKey = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes(options.Secret)),
    ClockSkew = TimeSpan.FromSeconds(30),
};
```

Pass ⇒ `HttpContext.User` được set thành `ClaimsPrincipal` chứa các claim từ
payload. Fail ⇒ `User` vẫn là anonymous (chưa 401 ngay — bước Authorization mới
quyết định).

**(b) Authorization theo route YARP** — `appsettings.json:32-35`:

```json
"orders-route": {
  "ClusterId": "orders-cluster",
  "AuthorizationPolicy": "Default",                 // = RequireAuthenticatedUser
  "Match": { "Path": "/orders/{**catch-all}" }
}
```

- `Default` = chính sách mặc định của `AddAuthorization()` ⇒ yêu cầu
  authenticated. Token sai/hết hạn/không có ⇒ **401 ngay tại gateway**, không
  forward.
- `Anonymous` = bỏ qua kiểm tra (dùng cho `/auth/*`, `/orders/health`,
  `/orders/swagger/*`).

Match theo specificity: YARP ưu tiên route có path chi tiết hơn, nên
`/orders/health` ăn `orders-health-route` (anonymous), còn `/orders/abc-123`
ăn `orders-route` (default).

**(c) YARP forward** — `MapReverseProxy()` đẩy request sang
`http://localhost:5102/orders` (Docker thì `http://orderservice:8080/orders`).
**Header `Authorization` được giữ nguyên** ⇒ service con đọc được.

### Bước 3 — Tại OrderService

**(a) Authentication lần 2** — pipeline giống hệt gateway, cùng secret/issuer/
audience ⇒ verify lại offline. Nếu pass, `HttpContext.User` ở service cũng có
đầy đủ claims.

**(b) Authorization ở controller** — `OrdersController.cs:13-14`:

```csharp
[ApiController]
[Route("orders")]
[Authorize]                                  // mọi action mặc định cần token
public sealed class OrdersController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(...) { ... }

    [AllowAnonymous]
    [HttpGet("health")]                      // health ngoại lệ
    public IActionResult Health() => ...;
}
```

`[Authorize]` ở class-level ⇒ tất cả action thừa kế. `[AllowAnonymous]` ở
method override class-level — ASP.NET Core ưu tiên cấp method.

**(c) Đọc claim trong action** — userId lấy thẳng từ `User`:

```csharp
var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
// hoặc User.FindFirstValue("sub") nếu mapping mặc định bị tắt
var email  = User.FindFirstValue(ClaimTypes.Email);
```

> Lưu ý: `JwtSecurityTokenHandler` mặc định remap `sub` → `ClaimTypes.NameIdentifier`
> và `email` → `ClaimTypes.Email`. Muốn lấy đúng tên claim gốc cần
> `JwtSecurityTokenHandler.DefaultMapInboundClaims = false;` ở Program.cs.

## 4. Tại sao phải validate 2 lần?

| Lý do                       | Giải thích                                                                                       |
|-----------------------------|--------------------------------------------------------------------------------------------------|
| Defense in depth            | Nếu ai gọi thẳng `:5102` (rò mạng nội bộ, deploy nhầm `ports:`…), service vẫn từ chối.            |
| Service tự dùng claim       | Action cần `userId/email` ⇒ phải có `HttpContext.User` thật (không thể trust header).            |
| Tách trách nhiệm            | Gateway có thể bị thay (Nginx, Traefik…) mà service không phải đổi.                              |
| Chi phí gần như 0           | HMAC verify offline rất nhanh, không round-trip.                                                  |

Đánh đổi: cả 2 tầng phải share secret. Cách tốt hơn (đã liệt kê ở
`14-bao-mat-jwt.md` mục 5): chuyển sang **RS256** — service chỉ cần public key,
chỉ AuthService giữ private key. Khi đó rotate key dễ hơn nhiều.

## 5. Trường hợp đặc biệt

### 5.1 SignalR / WebSocket

WebSocket không gắn được header `Authorization` lúc upgrade ⇒ token gửi qua
query: `wss://.../notifications/hubs/notifications?access_token=...`.

`JwtAuthExtensions.cs:46-59` cài `JwtBearerEvents.OnMessageReceived` đọc query
khi path chứa `/hubs/`:

```csharp
o.Events = new JwtBearerEvents
{
    OnMessageReceived = ctx =>
    {
        var accessToken = ctx.Request.Query["access_token"];
        var path = ctx.HttpContext.Request.Path;
        if (!string.IsNullOrEmpty(accessToken) && path.HasValue &&
            path.Value!.Contains("/hubs/", StringComparison.OrdinalIgnoreCase))
            ctx.Token = accessToken;
        return Task.CompletedTask;
    }
};
```

Hub `NotificationHub` đánh `[Authorize]` bình thường, claim `email` được
`EmailUserIdProvider` dùng để map `Hub.UserIdentifier` ⇒ push noti theo email.
Chi tiết → [15 — SignalR](./15-signalr.md).

### 5.2 gRPC service-to-service

OrderService → AuthService qua gRPC port 5111
(`UserGrpcService.cs`). **Hiện không gắn `[Authorize]`** vì đây là kênh nội bộ
trên Docker network, không lộ ra ngoài. Nếu mở ra cluster lớn hơn ⇒ thêm:

```csharp
app.MapGrpcService<UserGrpcService>().RequireAuthorization();
```

và client phải đính token vào metadata `Authorization: Bearer ...`.

### 5.3 Endpoint anonymous

| Loại           | Cấu hình                                               |
|----------------|--------------------------------------------------------|
| `/auth/*`      | route `Anonymous` (Gateway) + `[AllowAnonymous]` (controller register/login). |
| `/<svc>/health` | route `Anonymous` riêng cho từng service.             |
| `/<svc>/swagger/*` | route `Anonymous` riêng — để mở Swagger UI không cần token. |
| Gateway `/`, `/health` | gọi `.AllowAnonymous()` trên minimal API.        |

## 6. Bảng lỗi thường gặp

| HTTP | Nguyên nhân                                 | Cách kiểm tra                                                          |
|------|---------------------------------------------|-------------------------------------------------------------------------|
| 401  | Thiếu header `Authorization`                | `curl -v` xem request, đảm bảo header đúng `Authorization: Bearer ...`. |
| 401  | Sai secret giữa Gateway và service          | Diff `Jwt:Secret` ở `appsettings.json` của Gateway vs service.          |
| 401  | Sai `Issuer` / `Audience`                   | Decode token tại jwt.io, so với `JwtOptions`.                          |
| 401  | Token hết hạn                               | Decode → field `exp`, so với `DateTime.UtcNow`. Mặc định 60 phút (`JwtOptions.ExpiresMinutes`). |
| 401  | Lệch giờ giữa máy issue và máy verify > 30s | `ClockSkew = 30s` (`JwtAuthExtensions.cs:40`). Sync NTP.                |
| 403  | Có token nhưng policy yêu cầu role/claim    | Hệ hiện chưa có policy ⇒ 403 chưa xảy ra. Khi thêm role mới gặp.       |
| 404  | Health/swagger 404 sau khi thêm route mới   | Quên thêm route `Anonymous` riêng cho `/health` hoặc `/swagger/*`.     |

Debug nhanh: bật log JWT verbose tạm thời:

```json
"Logging": {
  "LogLevel": {
    "Microsoft.AspNetCore.Authentication": "Debug"
  }
}
```

→ log sẽ chỉ ra chính xác claim nào fail (`IDX10223: Lifetime validation failed`,
`IDX10500: Signature validation failed`, `IDX10205: Issuer validation failed`…).

## 7. Bản đồ file liên quan

| File                                                                  | Vai trò                                                |
|-----------------------------------------------------------------------|--------------------------------------------------------|
| `src/ApiGateway/Program.cs`                                           | Pipeline gateway, gọi `AddHdosJwtAuth`, `MapReverseProxy`. |
| `src/ApiGateway/appsettings.json`                                     | Cấu hình route + `AuthorizationPolicy` per route.       |
| `src/BuildingBlocks/Common/Auth/JwtAuthExtensions.cs`                 | `AddHdosJwtAuth` — wire up validator + SignalR hook.    |
| `src/BuildingBlocks/Common/Auth/JwtTokenIssuer.cs`                    | Phát token HS256 (chỉ AuthService gọi).                 |
| `src/BuildingBlocks/Common/Auth/JwtOptions.cs`                        | POCO map từ section `Jwt`.                              |
| `src/Services/AuthService/AuthService.API/Program.cs`                 | Pipeline auth service, expose REST + gRPC.              |
| `src/Services/AuthService/AuthService.API/Controllers/AuthController.cs` | `[Authorize]` class-level + `[AllowAnonymous]` cho login/register/health. |
| `src/Services/OrderService/OrderService.API/Program.cs`               | Pipeline order service.                                  |
| `src/Services/OrderService/OrderService.API/Controllers/OrdersController.cs` | Mẫu `[Authorize]` + ngoại lệ `[AllowAnonymous]` cho health. |
| `src/Services/NotificationService/NotificationService.API/Hubs/`      | Hub SignalR + `EmailUserIdProvider` đọc claim `email`.  |

## 8. Tài liệu tham khảo (chuẩn + Microsoft)

- **JWT spec — RFC 7519**: https://datatracker.ietf.org/doc/html/rfc7519 — định nghĩa
  claim chuẩn `sub`, `iss`, `aud`, `exp`, `nbf`, `jti`.
- **JWS — RFC 7515**: https://datatracker.ietf.org/doc/html/rfc7515 — chữ ký số,
  HS256/RS256.
- **Bearer token — RFC 6750**: https://datatracker.ietf.org/doc/html/rfc6750 —
  format header `Authorization: Bearer ...`.
- **ASP.NET Core JWT Bearer**: https://learn.microsoft.com/aspnet/core/security/authentication/jwt-authn
- **TokenValidationParameters**: https://learn.microsoft.com/dotnet/api/microsoft.identitymodel.tokens.tokenvalidationparameters
- **Authorization policies**: https://learn.microsoft.com/aspnet/core/security/authorization/policies
- **YARP — Authorization in routes**: https://microsoft.github.io/reverse-proxy/articles/authn-authz.html
- **SignalR — Bearer token authentication**:
  https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz#bearer-token-authentication
- **Serilog request logging** (đang dùng trong `RequestLoggingMiddleware`):
  https://github.com/serilog/serilog-aspnetcore

## 9. Hướng phát triển

- Thêm claim `roles`/`permissions` khi issue token, áp `[Authorize(Policy = "OrderAdmin")]`.
- Refresh token + revoke list (lưu `jti` blacklist trong Redis).
- Đổi HS256 → RS256, AuthService giữ private, các service và Gateway chỉ giữ
  public key (JWKS endpoint `/.well-known/jwks.json`).
- Áp `[Authorize]` cho `UserGrpcService` khi mở gRPC ra ngoài cluster.
- Đưa secret ra Vault / AWS Secrets Manager / Azure Key Vault.
