# 03 — Building Blocks

Building Blocks là các thư viện dùng chung. Mục tiêu: viết một lần, tất cả services sử dụng. Khi cần thay đổi cách logging hay response format — sửa một chỗ, rebuild tất cả.

---

## Result Pattern (`SharedKernel/Result.cs`)

**Vấn đề giải quyết:** Xử lý lỗi business logic mà không throw exception. Exception dành cho lỗi đột ngột (database down, null reference), không phải "sai password" hay "email đã tồn tại".

```csharp
// Thay vì throw:
// throw new Exception("User not found");

// Dùng Result:
return Result.Failure<UserDto>(UserErrors.NotFound(id));

// Handler nhận về:
var result = await _sender.Send(query);
if (result.IsFailure)
    return NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message));
return Ok(ApiResponse.Ok(result.Value));
```

`Result<T>` có hai trạng thái: `IsSuccess` và `IsFailure`. Không bao giờ throw exception cho business logic — chỉ trả về `Result.Failure(error)`.

---

## ApiResponse (`Common/Responses/ApiResponse.cs`)

Tất cả API trả về cùng một format:

```json
{
  "success": true,
  "data": { ... },
  "errorCode": null,
  "errorMessage": null
}
```

```json
{
  "success": false,
  "data": null,
  "errorCode": "Unauthorized",
  "errorMessage": "Invalid credentials"
}
```

**Lý do:** Frontend biết chính xác cấu trúc response mà không cần xử lý riêng từng endpoint.

---

## MediatR Behaviors

Pipeline behaviors là middleware cho CQRS — chạy trước/sau mọi Command/Query.

### LoggingBehavior
Log thời gian xử lý của mỗi Command/Query:
```
[INF] Handled LoginUserCommand in 81ms
```

### ValidationBehavior
Chạy FluentValidation trước khi Handler nhận request:
```csharp
public class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).MinimumLength(8);
    }
}
```
Nếu validation fail → throw `ValidationException` → `ExceptionHandlingMiddleware` bắt → trả 400.

**Lý do dùng behavior thay vì validate trong Handler:** DRY — không cần nhớ validate trong mỗi handler. Tất cả validation chạy tự động.

---

## PublishDomainEventsInterceptor

**Vấn đề giải quyết:** Domain Events cần được dispatch SAU KHI dữ liệu đã lưu vào DB (không phải trước). Nếu dispatch trước, handler có thể phản ứng với dữ liệu chưa tồn tại.

```csharp
public sealed class PublishDomainEventsInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(...)
    {
        // SaveChanges đã chạy xong, dữ liệu đã commit
        await PublishDomainEvents(dbContext);
        return result;
    }
}
```

Flow khi user đăng ký:
```
RegisterUserCommandHandler
  → user.Register()           ← thêm UserRegisteredDomainEvent vào list
  → dbContext.SaveChangesAsync()
      → PublishDomainEventsInterceptor.SavedChangesAsync()
          → mediator.Publish(UserRegisteredDomainEvent)
              → UserRegisteredDomainEventHandler
                  → eventBus.PublishAsync(UserRegisteredIntegrationEvent)
                      → RabbitMQ
```

---

## ExceptionHandlingMiddleware

Bắt tất cả unhandled exception và trả về JSON:

```csharp
private static (HttpStatusCode status, string code, string message) = ex switch
{
    ValidationException v   => (400, "Validation", errors),
    NotFoundException nf    => (404, "NotFound",   nf.Message),
    ConflictException cf    => (409, "Conflict",   cf.Message),
    UnauthorizedAccessException ua => (401, "Unauthorized", ua.Message),
    _                       => (500, "Server",     "An unexpected error occurred.")
};
```

**Quan trọng:** Stack trace không bao giờ trả về client. `_` case trả về generic message.

---

## RequestLoggingMiddleware

Log mỗi HTTP request với TraceId và SpanId (từ OpenTelemetry context):

```
[INF] HTTP POST /auth/login responded 200 in 83ms
      TraceId=4092180ceb8cd9cb65a0658d5ac7cc12
      SpanId=0a642908c7ae3426
```

TraceId/SpanId này được Grafana Loki dùng để link log sang Tempo trace. Xem [09 — W3C Trace Context](./09-w3c-trace-context.md).

---

## JWT Extensions (`Common/Auth/JwtAuthExtensions.cs`)

`AddHdosJwtAuth()` — dùng ở tất cả services để validate JWT:

```csharp
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,       // Phải là "Hdos.Auth"
            ValidateAudience = true,     // Phải là "Hdos.Services"
            ValidateLifetime = true,     // Token chưa expired
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(secret),
            ClockSkew = TimeSpan.FromSeconds(30)  // Cho phép lệch đồng hồ 30s
        };

        // SignalR WebSocket: token qua query string thay vì header
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    ctx.Request.Path.Value!.Contains("/hubs/"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });
```

`AddHdosJwtIssuer()` — chỉ dùng ở AuthService để TẠO JWT.

---

## Health Checks (`Common/HealthChecks`)

Ba endpoint được register tự động qua `MapHdosHealthChecks()`:

| Endpoint | Kiểm tra | Dùng cho |
|----------|---------|---------|
| `/health/live` | Không gì (process còn sống) | Kubernetes liveness probe |
| `/health/ready` | SQL Server + RabbitMQ | Kubernetes readiness probe |
| `/health` | SQL Server + RabbitMQ | Nginx gateway health route |

Nginx gateway có route `location ~ ^/xxx(/health.*)` để strip prefix:
- Request: `GET /orders/health/live`
- OrderService nhận: `GET /health/live`

---

## RabbitMQ Messaging

### Publisher (`RabbitMqEventBus`)

```csharp
// Cách dùng trong handler:
await _eventBus.PublishAsync(new UserRegisteredIntegrationEvent(userId, email));
```

Phía dưới:
1. Tạo Activity (W3C tracing) với kind `Producer`
2. Inject `traceparent`/`tracestate` vào AMQP headers
3. Serialize event thành JSON
4. Publish tới exchange `hdos.events` với routing key = tên class event

### Consumer (`RabbitMqConsumerHostedService<TEvent, THandler>`)

Base class — subclass chỉ cần khai báo event type và handler type:

```csharp
public class UserRegisteredConsumerService
    : RabbitMqConsumerHostedService<UserRegisteredIntegrationEvent, UserRegisteredConsumer>
{
    // Không cần viết gì thêm
}
```

Base class xử lý:
- Connect/reconnect tới RabbitMQ
- Declare queue và bind tới exchange
- Deserialize message
- Extract W3C trace context để link với producer trace
- Gọi handler
- Manual ack/nack (không dùng auto-ack)
- Retry: nếu lần đầu fail → requeue một lần. Nếu fail lần 2 → nack (drop/dead-letter)

**Lý do manual ack:** Nếu auto-ack, message bị xóa khỏi queue ngay khi deliver. Nếu service crash trong lúc xử lý → mất message. Manual ack đảm bảo chỉ ack sau khi xử lý thành công.

---

## Logging (`Common/Logging`)

`UseHdosLogging(serviceName)` cấu hình Serilog:

```csharp
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.WithProperty("ServiceName", serviceName)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp} {Level}] [{ServiceName}] {Message}{NewLine}{Exception}")
    .WriteTo.GrafanaLoki(lokiUri,   // Nếu có Loki__Uri trong config
        labels: [new("service", serviceName)]));
```

Mỗi log entry có:
- `ServiceName`: biết log từ service nào
- `RequestId`, `RequestPath`: từ ASP.NET Core
- `SpanId`, `TraceId`: từ OpenTelemetry (inject bởi RequestLoggingMiddleware)
