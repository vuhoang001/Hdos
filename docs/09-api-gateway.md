# 09 — API Gateway (YARP)

`ApiGateway` là cổng vào duy nhất cho client. Triển khai bằng
[YARP](https://microsoft.github.io/reverse-proxy/) — reverse proxy có cấu hình
dạng JSON, gọn và đủ mạnh cho microservices.

## 1. Vai trò

- Forward HTTP theo **prefix path** sang service nội bộ tương ứng.
- Tạo single endpoint cho client: `http://localhost:5000`.
- Tách hostname/port thật của các service khỏi client.
- Áp dụng middleware chung (request log, exception handling) cho mọi request.

## 2. Code

File: `src/ApiGateway/Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.UseHdosLogging("ApiGateway");

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();
app.UseHdosMiddleware();

app.MapGet("/",       () => Results.Ok(new { name = "Hdos API Gateway", ... }));
app.MapGet("/health", () => Results.Ok(new { status = "OK", service = "ApiGateway" }));

app.MapReverseProxy();
app.Run();
```

Toàn bộ logic routing nằm trong **config**, không phải code.

## 3. Cấu hình routes

File: `src/ApiGateway/appsettings.json`

```json
{
  "ReverseProxy": {
    "Routes": {
      "auth-route":          { "ClusterId": "auth-cluster",          "Match": { "Path": "/auth/{**catch-all}" } },
      "orders-route":        { "ClusterId": "orders-cluster",        "Match": { "Path": "/orders/{**catch-all}" } },
      "notifications-route": { "ClusterId": "notifications-cluster", "Match": { "Path": "/notifications/{**catch-all}" } }
    },
    "Clusters": {
      "auth-cluster":          { "Destinations": { "auth-1":          { "Address": "http://localhost:5101/" } } },
      "orders-cluster":        { "Destinations": { "orders-1":        { "Address": "http://localhost:5102/" } } },
      "notifications-cluster": { "Destinations": { "notifications-1": { "Address": "http://localhost:5103/" } } }
    }
  }
}
```

`appsettings.Docker.json` chỉ khác `Address` (dùng hostname container thay vì
`localhost`):

```json
"auth-1": { "Address": "http://authservice:8080/" }
```

YARP chọn config theo `ASPNETCORE_ENVIRONMENT` — compose set
`ASPNETCORE_ENVIRONMENT=Docker` cho gateway.

## 4. Pattern matching

`/auth/{**catch-all}` ⇒ mọi path bắt đầu `/auth/` được forward, **giữ nguyên
phần đuôi**:

| Request                          | Forwarded to                              |
|----------------------------------|-------------------------------------------|
| `/auth/register`                 | `http://localhost:5101/auth/register`     |
| `/auth/users/abc-123`            | `http://localhost:5101/auth/users/abc-123` |
| `/orders` (POST)                 | `http://localhost:5102/orders`            |
| `/notifications?take=20`         | `http://localhost:5103/notifications?take=20` |
| `/`                              | Gateway endpoint (không proxy)             |
| `/health`                        | Gateway endpoint (không proxy)             |

YARP **không strip prefix** — service nhận đúng path mà client gửi. Vì thế
controller `[Route("auth")]` ở `AuthService` vẫn match.

## 5. Có thể proxy gRPC qua YARP không?

**Có**. YARP hỗ trợ HTTP/2 → cho phép forward gRPC. Cần:

- Cluster destination phải là `http(s)://...:<grpc-port>`.
- Trong route, set `"Match": { "Path": "/<package>.<service>/{**catch-all}" }`
  (vd `/hdos.users.v1.UserService/{**catch-all}`).
- Đảm bảo `HttpRequest:Version = "2"` cho route đó (YARP có option).

Hiện tại hệ thống **không** proxy gRPC qua gateway — gRPC chỉ là internal.
Client web không cần truy cập trực tiếp.

## 6. Health & root endpoint

Gateway tự host 2 endpoint nhỏ (không qua YARP):

- `GET /` → trả info routes (debug).
- `GET /health` → 200 OK, dùng cho liveness probe.

Mỗi service phía sau có endpoint health riêng (`/auth/health`,
`/orders/health`, `/notifications/health`) cũng forward qua gateway. Gọi
`/auth/health` từ client = test cả gateway + auth service đang sống.

## 7. Mở rộng

| Cần                         | Cách thêm                                                                         |
|-----------------------------|-----------------------------------------------------------------------------------|
| Multiple replicas           | Thêm key trong `Destinations`: `auth-2`, `auth-3`. YARP load-balance round-robin. |
| Sticky sessions             | `LoadBalancingPolicy: "Cookie"` ở cluster level.                                   |
| Auth/JWT ở gateway          | Thêm `AddAuthentication().AddJwtBearer(...)` + `RequireAuthorization()` cho route. |
| Rate limiting               | `app.UseRateLimiter(...)` trước `MapReverseProxy()`.                               |
| Aggregated `/healthz`       | `services.AddHealthChecks().AddCheck<HttpHealthCheck>(...)` rồi map endpoint.      |
| Per-route header rewrite    | `Transforms` block trong route.                                                    |
| CORS                        | `AddCors(...)` + `UseCors(...)`.                                                   |

## 8. Thêm service mới — checklist phần gateway

1. Mở `appsettings.json`: thêm route `<svc>-route` + cluster `<svc>-cluster`.
2. Mở `appsettings.Docker.json`: thêm cluster với hostname container.
3. Thêm `<svc>` vào docker-compose `depends_on` của apigateway (không bắt buộc
   nhưng giúp gateway khởi động sau service).
4. Reload (gateway dùng `LoadFromConfig` ⇒ chỉ cần restart container hoặc
   process — chưa wire hot reload).

Chi tiết tổng hợp ở [10 — Thêm feature/service mới](./10-them-feature-moi.md).
