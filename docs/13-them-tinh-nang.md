# 13 — Thêm tính năng mới

Checklist step-by-step cho 3 loại thay đổi phổ biến nhất: thêm endpoint, thêm integration event, thêm service mới.

---

## A. Thêm endpoint vào service có sẵn

Ví dụ: Thêm `DELETE /orders/{id}` vào OrderService.

### 1. Domain — thêm method vào Aggregate

```csharp
// src/Services/OrderService/OrderService.Domain/Entities/Order.cs
public void Cancel()
{
    if (Status == OrderStatus.Cancelled) return;  // idempotent
    if (Status != OrderStatus.Pending)
        throw new InvalidOperationException($"Cannot cancel order in status {Status}");

    Status = OrderStatus.Cancelled;
    UpdatedAtUtc = DateTime.UtcNow;
    RaiseDomainEvent(new OrderCancelledDomainEvent(Id));
}
```

### 2. Application — thêm Command + Handler

```csharp
// src/Services/OrderService/OrderService.Application/Features/CancelOrder/CancelOrderCommand.cs
public record CancelOrderCommand(Guid OrderId) : IRequest<Result>;
```

```csharp
// CancelOrderCommandHandler.cs
public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, Result>
{
    private readonly IOrderRepository _orders;
    private readonly IUnitOfWork _uow;

    public async Task<Result> Handle(CancelOrderCommand request, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(request.OrderId, ct);
        if (order is null)
            return Result.Failure(Error.NotFound($"Order {request.OrderId} not found"));

        try
        {
            order.Cancel();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict(ex.Message));
        }

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

### 3. API — thêm action vào Controller

```csharp
// src/Services/OrderService/OrderService.API/Controllers/OrdersController.cs
[HttpDelete("{id:guid}")]
public async Task<IActionResult> CancelOrder(Guid id)
{
    var result = await _sender.Send(new CancelOrderCommand(id));

    return result.IsSuccess
        ? NoContent()
        : result.Error.Code switch
        {
            "NotFound" => NotFound(ApiResponse.Failure(result.Error.Message)),
            "Conflict" => Conflict(ApiResponse.Failure(result.Error.Message)),
            _ => StatusCode(500, ApiResponse.Failure(result.Error.Message))
        };
}
```

### 4. Test

```csharp
// tests/Hdos.OrderService.Tests/Application/CancelOrderCommandHandlerTests.cs
[Fact]
public async Task Handle_PendingOrder_CancelsAndSaves()
{
    var order = Order.Create(Guid.NewGuid(), "a@b.io", new[] { ("X", 1, 1m, "USD") });
    _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

    var result = await NewHandler().Handle(new CancelOrderCommand(order.Id), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    order.Status.Should().Be(OrderStatus.Cancelled);
    await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
}
```

### 5. nginx — không cần thay đổi

`location /orders/` đã match tất cả sub-paths. DELETE method đã có trong `Access-Control-Allow-Methods`.

---

## B. Thêm Integration Event mới

Ví dụ: OrderService publish `OrderCancelledIntegrationEvent`, NotificationService consume.

### 1. Contracts — định nghĩa event (shared)

```csharp
// src/BuildingBlocks/Contracts/IntegrationEvents/OrderCancelledIntegrationEvent.cs
public record OrderCancelledIntegrationEvent(
    Guid EventId,
    Guid OrderId,
    Guid CustomerId,
    string CustomerEmail,
    DateTime OccurredOn) : IntegrationEvent;
```

`IntegrationEvent` base class tự generate `EventId` và `OccurredOn` nếu không truyền vào.

### 2. Publisher — publish trong Handler

```csharp
// CancelOrderCommandHandler.cs — sau khi SaveChanges
await _eventBus.PublishAsync(new OrderCancelledIntegrationEvent(
    OrderId: order.Id,
    CustomerId: order.CustomerId,
    CustomerEmail: order.CustomerEmail,
    OccurredOn: DateTime.UtcNow), ct);
```

### 3. Consumer — tạo handler trong service nhận

```csharp
// src/Services/NotificationService/NotificationService.Infrastructure/
//   Consumers/OrderCancelledConsumer.cs
public class OrderCancelledConsumer
    : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    public async Task HandleAsync(OrderCancelledIntegrationEvent @event, CancellationToken ct)
    {
        var notification = Notification.Create(
            userId: @event.CustomerId,
            message: $"Đơn hàng của bạn đã bị hủy.");

        await _repo.AddAsync(notification, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await _pusher.PushAsync(@event.CustomerId.ToString(), notification, ct);
    }
}
```

### 4. Đăng ký Hosted Service

```csharp
// src/Services/NotificationService/NotificationService.Infrastructure/
//   Consumers/OrderCancelledConsumerService.cs
public class OrderCancelledConsumerService
    : RabbitMqConsumerHostedService<OrderCancelledIntegrationEvent, OrderCancelledConsumer>
{
    public OrderCancelledConsumerService(
        RabbitMqConnection conn,
        OrderCancelledConsumer handler,
        IOptions<RabbitMqOptions> opts)
        : base(conn, handler, opts, queueName: "notification.order-cancelled") { }
}
```

```csharp
// DependencyInjection.cs của NotificationService.Infrastructure
services.AddScoped<OrderCancelledConsumer>();
services.AddHostedService<OrderCancelledConsumerService>();
```

### 5. RabbitMQ topology tự cập nhật

`RabbitMqConsumerHostedService` base class tự declare queue và binding khi khởi động — không cần thêm config thủ công.

---

## C. Thêm service mới hoàn toàn

Ví dụ: Thêm `PaymentService`.

### 1. Tạo project structure

```bash
mkdir -p src/Services/PaymentService/{PaymentService.Domain,PaymentService.Application,PaymentService.Infrastructure,PaymentService.API}

# Tạo .csproj files (copy từ service có sẵn và sửa namespace)
# Hoặc dùng dotnet CLI:
cd src/Services/PaymentService
dotnet new classlib -n PaymentService.Domain --framework net8.0
dotnet new classlib -n PaymentService.Application --framework net8.0
dotnet new classlib -n PaymentService.Infrastructure --framework net8.0
dotnet new webapi -n PaymentService.API --framework net8.0
```

### 2. Thêm vào Solution

```bash
cd /path/to/Hdos
dotnet sln add src/Services/PaymentService/PaymentService.Domain/PaymentService.Domain.csproj
dotnet sln add src/Services/PaymentService/PaymentService.Application/PaymentService.Application.csproj
dotnet sln add src/Services/PaymentService/PaymentService.Infrastructure/PaymentService.Infrastructure.csproj
dotnet sln add src/Services/PaymentService/PaymentService.API/PaymentService.API.csproj
```

### 3. Program.cs — copy pattern từ service có sẵn

```csharp
// src/Services/PaymentService/PaymentService.API/Program.cs
var builder = WebApplication.CreateBuilder(args);
var serviceName = "paymentservice";

// ── BuildingBlocks ──
builder.Host.UseSerilog(serviceName);
builder.Services.AddHdosJwtAuth(builder.Configuration);
builder.Services.AddHdosHealthChecks(builder.Configuration, includeRabbitMq: true);
builder.Services.AddHdosOpenTelemetry(builder.Configuration, serviceName);
builder.Services.AddHdosSwagger("PaymentService", "v1");
builder.Services.AddHdosCors();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CreatePaymentCommand>());
builder.Services.AddValidatorsFromAssemblyContaining<CreatePaymentCommandValidator>();

// ── Infrastructure ──
builder.Services.AddDbContext<PaymentDbContext>(...);
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork<PaymentDbContext>>();
builder.Services.AddRabbitMqEventBus(builder.Configuration);

var app = builder.Build();
app.UseHdosCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseHttpMetrics();
app.UseHdosMiddlewares();
app.UseHdosHealthChecks();
app.UseHdosSwagger("PaymentService", "payments/swagger");
app.MapControllers();
await app.MigrateDbAsync<PaymentDbContext>();
await app.RunAsync();
```

### 4. Dockerfile

```dockerfile
# src/Services/PaymentService/PaymentService.API/Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/Services/PaymentService/PaymentService.API/PaymentService.API.csproj", "src/Services/PaymentService/PaymentService.API/"]
COPY ["src/BuildingBlocks/", "src/BuildingBlocks/"]
# ... copy các csproj khác
RUN dotnet restore "src/Services/PaymentService/PaymentService.API/PaymentService.API.csproj"
COPY . .
RUN dotnet publish "src/Services/PaymentService/PaymentService.API/PaymentService.API.csproj" \
    -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PaymentService.API.dll"]
```

### 5. docker-compose.yml — thêm service

```yaml
paymentservice:
  image: hdos/paymentservice:latest
  build:
    context: .
    dockerfile: src/Services/PaymentService/PaymentService.API/Dockerfile
  environment:
    ASPNETCORE_ENVIRONMENT: Development
    ConnectionStrings__PaymentDb: "Server=sqlserver,1433;Database=PaymentDb;..."
    RabbitMq__Host: rabbitmq
    Jwt__Secret: "${JWT_SECRET:-hdos-dev-secret-change-me-in-production-!!}"
    Jwt__Issuer: "Hdos.Auth"
    Jwt__Audience: "Hdos.Services"
  depends_on:
    sqlserver:
      condition: service_healthy
    rabbitmq:
      condition: service_healthy
  networks: [hdos-net]
```

### 6. nginx — thêm upstream và routes

```nginx
# nginx/nginx.conf

upstream paymentservice { server paymentservice:8080; }

# Health (anonymous)
location ~ ^/payments(/health.*) {
    proxy_pass         http://paymentservice$1;
    proxy_set_header   Host            $host;
    proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_http_version 1.1;
}

# Swagger (anonymous)
location /payments/swagger {
    proxy_pass         http://paymentservice;
    proxy_set_header   Host            $host;
    proxy_http_version 1.1;
}

# Business routes (JWT required)
location /payments/ {
    auth_request /_auth_validate;
    error_page 401 = @unauthorized;
    error_page 403 = @forbidden;

    proxy_pass         http://paymentservice;
    proxy_set_header   Host              $host;
    proxy_set_header   X-Real-IP         $remote_addr;
    proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
    proxy_set_header   X-Forwarded-Proto $scheme;
    proxy_http_version 1.1;
    proxy_set_header   Upgrade    $http_upgrade;
    proxy_set_header   Connection $connection_upgrade;
}
```

### 7. Monitoring — thêm Prometheus scrape

```yaml
# monitoring/prometheus.yml
- job_name: paymentservice
  static_configs:
    - targets: ['paymentservice:8080']
  metrics_path: /metrics
```

### 8. CI/CD — thêm vào pipeline

**`services.json`:**
```json
"paymentservice": {
  "dockerfile": "src/Services/PaymentService/PaymentService.API/Dockerfile",
  "context": "."
}
```

**`.github/path-filters.yml`:**
```yaml
paymentservice:
  - "src/Services/PaymentService/**"
  - "src/BuildingBlocks/**"
```

**`docker-compose.server.yml`:**
```yaml
paymentservice:
  image: ghcr.io/${GHCR_OWNER}/hdos-paymentservice:${IMAGE_TAG}
  build: !reset null
  env_file:
    - ${ENV_DIR}/common.env
    - ${ENV_DIR}/paymentservice.env
  environment:
    ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT}
```

**Trên server:**
```bash
touch /opt/hdos-prod/paymentservice.env
echo "ConnectionStrings__PaymentDb=..." >> /opt/hdos-prod/paymentservice.env
```

### 9. Test project

```bash
dotnet new xunit -n Hdos.PaymentService.Tests --framework net8.0 -o tests/Hdos.PaymentService.Tests
dotnet sln add tests/Hdos.PaymentService.Tests/Hdos.PaymentService.Tests.csproj
# Thêm packages: FluentAssertions, NSubstitute, coverlet
```

### 10. Trigger build lần đầu

```bash
git add .
git commit -m "feat: add PaymentService"
git push origin main
# Sau đó: GitHub → Actions → CI → Run workflow → force_build_all=true
```

---

## Checklist tóm tắt

### Thêm endpoint
- [ ] Domain: thêm method vào Aggregate, raise DomainEvent nếu cần
- [ ] Application: thêm Command/Query + Handler
- [ ] API: thêm action vào Controller, map Error codes → HTTP status
- [ ] Tests: happy path + failure cases

### Thêm Integration Event
- [ ] Contracts: định nghĩa record trong `BuildingBlocks/Contracts/IntegrationEvents/`
- [ ] Publisher: `await _eventBus.PublishAsync(...)` trong handler
- [ ] Consumer: implement `IIntegrationEventHandler<TEvent>`
- [ ] Hosted Service: tạo class kế thừa `RabbitMqConsumerHostedService`
- [ ] DI: đăng ký consumer và hosted service

### Thêm service mới
- [ ] 4 projects: Domain, Application, Infrastructure, API
- [ ] Program.cs với đầy đủ BuildingBlocks
- [ ] Dockerfile
- [ ] docker-compose.yml (local)
- [ ] docker-compose.server.yml (prod)
- [ ] nginx.conf: upstream + 3 locations (health, swagger, business)
- [ ] monitoring/prometheus.yml: scrape job
- [ ] services.json
- [ ] .github/path-filters.yml
- [ ] Server: tạo .env file
- [ ] Tests: project + csproj
- [ ] Trigger build: workflow_dispatch với force_build_all=true
