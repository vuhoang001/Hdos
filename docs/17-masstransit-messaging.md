# 17 — MassTransit Messaging

Hệ thống dùng **MassTransit 8.2** làm lớp abstraction trên **RabbitMQ** để truyền integration events giữa các microservices. MassTransit quản lý toàn bộ vòng đời của consumer, retry, dead-letter và health check — không cần viết `BackgroundService` hay xử lý AMQP thủ công.

---

## Cấu hình

### 1. appsettings.json

Tất cả services dùng chung section `RabbitMq`:

```json
{
  "RabbitMq": {
    "Host": "rabbitmq",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "VirtualHost": "/"
  }
}
```

Trong môi trường local (`appsettings.Development.json`), `Host` đổi thành `localhost`.

### 2. Extension method

`AddMassTransitMessaging` nằm trong `Common/Extensions/ServiceCollectionExtensions.cs`. Tuỳ service gọi theo một trong hai dạng:

```csharp
// Service chỉ publish, không consume (ApiGateway, AuthService)
services.AddMassTransitMessaging(configuration);

// Service vừa publish vừa consume (NotificationService, OrderService)
services.AddMassTransitMessaging(configuration, x =>
{
    x.AddConsumer<MyConsumer>();
});
```

**Những gì extension này làm:**
- Kết nối RabbitMQ theo config trên
- Đặt formatter tên queue theo `KebabCase` (vd. `OrderCreatedConsumer` → queue `order-created`)
- Cấu hình retry **exponential backoff**: tối đa 5 lần, bắt đầu 1 giây, tăng dần, giới hạn 30 giây
- Tạo receive endpoint cho tất cả consumer đã đăng ký
- Đăng ký `IEventBus` (publish) và health check của bus vào DI

---

## Cách hoạt động

### Publish

```
Service gọi IEventBus.PublishAsync(event)
    └─ MassTransitEventBus.PublishAsync()
         └─ IPublishEndpoint.Publish(event, runtimeType)
              └─ RabbitMQ Exchange: Hdos.Contracts.IntegrationEvents:{MessageType} [fanout]
                   └─ Route tới tất cả queue đang bind vào exchange đó
```

MassTransit tạo **một fanout exchange riêng cho mỗi message type**, không dùng topic exchange chung.  
Ví dụ: `Hdos.Contracts.IntegrationEvents:OrderCreatedIntegrationEvent`.

### Consume

```
Message vào queue order-created
    └─ MassTransit tạo scope DI mới
         └─ Resolve OrderCreatedConsumer (Infrastructure)
              └─ Gọi OrderCreatedEventHandler.HandleAsync() (Application)
                   ├─ Thành công → Ack, xóa khỏi queue
                   └─ Exception  → Retry (tối đa 5 lần)
                                    └─ Vẫn fail → chuyển sang queue order-created_error
```

Consumer trong Infrastructure là **adapter mỏng** — chỉ nhận message và gọi handler. Toàn bộ business logic nằm trong Application handler.

---

## Danh sách events hiện tại

| Integration Event | Publisher | Consumer (Service) | Application Handler |
|---|---|---|---|
| `UserRegisteredIntegrationEvent` | AuthService | NotificationService | `UserRegisteredEventHandler` |
| `UserLoggedInIntegrationEvent` | AuthService | NotificationService | `UserLoggedInEventHandler` |
| `OrderCreateRequestedIntegrationEvent` | ApiGateway | OrderService | `OrderCreateRequestedEventHandler` |
| `OrderCreatedIntegrationEvent` | OrderService | NotificationService | `OrderCreatedEventHandler` |
| `NotificationSendRequestedIntegrationEvent` | ApiGateway | NotificationService | `NotificationSendRequestedEventHandler` |
| `TestIntegrationEvent` | ApiGateway (`/async/test/publish`) | NotificationService | `TestIntegrationEventHandler` |

Queue name của mỗi consumer = tên class theo kebab-case (vd. `UserLoggedInConsumer` → `user-logged-in`).

---

## Hướng dẫn: Thêm publisher mới

Giả sử service **OrderService** muốn bắn sự kiện `PaymentRequestedIntegrationEvent` sau khi tạo đơn hàng.

### Bước 1 — Định nghĩa contract

Thêm file trong project `Contracts` (shared, tất cả services đều reference):

```
src/BuildingBlocks/Contracts/IntegrationEvents/PaymentRequestedIntegrationEvent.cs
```

```csharp
namespace Hdos.Contracts.IntegrationEvents;

public sealed record PaymentRequestedIntegrationEvent(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency) : IntegrationEvent;
```

> `IntegrationEvent` là base record, tự sinh `EventId` (Guid) và `OccurredOnUtc` (DateTime).

### Bước 2 — Inject IEventBus vào nơi cần publish

```csharp
// OrderService.Application/Features/CreateOrder/CreateOrderCommandHandler.cs

public sealed class CreateOrderCommandHandler(
    IOrderRepository repo,
    IUnitOfWork uow,
    IEventBus eventBus,   // ← inject
    ILogger<CreateOrderCommandHandler> logger) : IRequestHandler<...>
{
    public async Task<Result<OrderDto>> Handle(CreateOrderCommand command, CancellationToken ct)
    {
        var order = Order.Create(command.CustomerId, command.Items);
        await repo.AddAsync(order, ct);
        await uow.SaveChangesAsync(ct);

        // Publish sau khi lưu DB thành công
        await eventBus.PublishAsync(new PaymentRequestedIntegrationEvent(
            OrderId: order.Id,
            CustomerId: order.CustomerId,
            Amount: order.TotalAmount,
            Currency: "VND"), ct);

        logger.LogInformation("Published PaymentRequested for Order {OrderId}", order.Id);
        return Result.Success(order.ToDto());
    }
}
```

**Chỉ cần 2 bước này là publish hoạt động.** MassTransit tự tạo exchange trên RabbitMQ khi message đầu tiên được gửi.

---

## Hướng dẫn: Thêm consumer mới

### Kiến trúc 2 tầng

Mỗi consumer trong Hdos được tách thành 2 lớp:

```
Infrastructure layer              Application layer
────────────────────              ─────────────────
XxxConsumer                       XxxEventHandler
  IConsumer<TEvent>   ──────────►   IIntegrationEventHandler<TEvent>
  thin adapter                      business logic
  import MassTransit                không import MassTransit
```

Consumer trong Infrastructure chỉ là dây nối giữa MassTransit và business logic. Tách handler ra Application layer để business logic không phụ thuộc framework và dễ unit test hơn.

Ví dụ từ code thực tế trong project:

| Consumer (Infrastructure) | Handler (Application) | Queue |
|---|---|---|
| `UserLoggedInConsumer` | `UserLoggedInEventHandler` | `user-logged-in` |
| `UserRegisteredConsumer` | `UserRegisteredEventHandler` | `user-registered` |
| `OrderCreatedConsumer` | `OrderCreatedEventHandler` | `order-created` |
| `OrderCreateRequestedConsumer` | `OrderCreateRequestedEventHandler` | `order-create-requested` |

---

Tiếp tục ví dụ từ phần publisher — giả sử **PaymentService** muốn nhận `PaymentRequestedIntegrationEvent`.

### Bước 1 — Kiểm tra / định nghĩa contract

Contract phải nằm trong `src/BuildingBlocks/Contracts/IntegrationEvents/`. Nếu event chưa có, tạo mới:

```csharp
// src/BuildingBlocks/Contracts/IntegrationEvents/PaymentRequestedIntegrationEvent.cs
namespace Hdos.Contracts.IntegrationEvents;

public sealed record PaymentRequestedIntegrationEvent(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency) : IntegrationEvent;
```

Nếu event đã có (do publisher tạo ở bước trước), bỏ qua bước này.

### Bước 2 — Viết Application Handler

Handler nằm trong `{Service}.Application/EventHandlers/`, **không import MassTransit**:

```
src/Services/PaymentService/PaymentService.Application/EventHandlers/PaymentRequestedEventHandler.cs
```

```csharp
using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;

namespace Hdos.PaymentService.Application.EventHandlers;

public sealed class PaymentRequestedEventHandler(
    IPaymentRepository repo,
    IUnitOfWork uow,
    ILogger<PaymentRequestedEventHandler> logger)
    : IIntegrationEventHandler<PaymentRequestedIntegrationEvent>
{
    public async Task HandleAsync(PaymentRequestedIntegrationEvent @event, CancellationToken ct)
    {
        logger.LogInformation(
            "Processing payment for Order {OrderId}, Amount {Amount}",
            @event.OrderId, @event.Amount);

        var payment = Payment.Create(@event.OrderId, @event.Amount, @event.Currency);
        await repo.AddAsync(payment, ct);
        await uow.SaveChangesAsync(ct);
    }
}
```

Tham khảo handler thực tế: `NotificationService.Application/EventHandlers/UserLoggedInEventHandler.cs`.

**Nguyên tắc:**
- Inject dependency qua constructor
- Log ít nhất một `LogInformation` khi nhận event (để trace khi debug)
- Chỉ gọi `SaveChangesAsync` một lần sau khi xong tất cả DB operations
- Không bắt exception ở đây — MassTransit retry policy tự xử lý

### Bước 3 — Đăng ký Handler vào Application DI

```csharp
// PaymentService.Application/DependencyInjection.cs
public static IServiceCollection AddPaymentApplication(this IServiceCollection services)
{
    var assembly = Assembly.GetExecutingAssembly();
    services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
    services.AddCommonMediatRBehaviors();

    services.AddScoped<PaymentRequestedEventHandler>();  // ← thêm dòng này

    return services;
}
```

Phải dùng `AddScoped` vì handler thường inject repository và DbContext — tất cả đều scoped.

### Bước 4 — Viết Consumer (Infrastructure)

Consumer là adapter mỏng, nằm trong `{Service}.Infrastructure/Consumers/`:

```
src/Services/PaymentService/PaymentService.Infrastructure/Consumers/PaymentRequestedConsumer.cs
```

```csharp
using Hdos.Contracts.IntegrationEvents;
using Hdos.PaymentService.Application.EventHandlers;
using MassTransit;

namespace Hdos.PaymentService.Infrastructure.Consumers;

public sealed class PaymentRequestedConsumer(PaymentRequestedEventHandler handler)
    : IConsumer<PaymentRequestedIntegrationEvent>
{
    public Task Consume(ConsumeContext<PaymentRequestedIntegrationEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
```

Consumer không chứa bất kỳ logic nào — chỉ nhận message và delegate sang handler.

**Quy tắc đặt tên:** Consumer class kết thúc bằng `Consumer`. Tên queue = kebab-case của phần trước `Consumer`:

| Class | Queue |
|---|---|
| `PaymentRequestedConsumer` | `payment-requested` |
| `UserLoggedInConsumer` | `user-logged-in` |
| `OrderCreateRequestedConsumer` | `order-create-requested` |

### Bước 5 — Đăng ký Consumer vào Infrastructure DI

```csharp
// PaymentService.Infrastructure/DependencyInjection.cs
services.AddMassTransitMessaging(configuration, x =>
{
    x.AddConsumer<PaymentRequestedConsumer>();  // ← thêm dòng này
    // thêm consumer khác nếu có
});
```

### Bước 6 — Kiểm tra thứ tự DI trong Program.cs

Application DI phải được gọi trước Infrastructure DI, vì MassTransit resolve handler qua DI khi nhận message:

```csharp
// Program.cs
builder.Services.AddPaymentApplication();      // 1. Application DI trước
builder.Services.AddPaymentInfrastructure();   // 2. Infrastructure DI sau
```

Nếu thứ tự ngược lại, service sẽ khởi động được nhưng throw `InvalidOperationException` khi consumer nhận message đầu tiên (handler chưa được đăng ký).

### Bước 7 — Kiểm tra trên RabbitMQ Management

Sau khi service khởi động, vào `http://localhost:15672`:
- **Exchanges** → tìm `Hdos.Contracts.IntegrationEvents:PaymentRequestedIntegrationEvent` → kiểu fanout
- **Queues** → tìm `payment-requested` → đang bind vào exchange trên

Log của service khi khởi động sẽ có:
```
[INF] Receive endpoint configured: payment-requested
```

---

### Dead-letter & Retry

Hành vi khi handler throw exception:

```
Handler throw exception
    └─ MassTransit retry: exponential backoff, tối đa 5 lần (1s → 5s → 10s → 20s → 30s)
         └─ Vẫn fail sau 5 lần
              └─ Message chuyển sang queue: payment-requested_error
```

Xem message lỗi: RabbitMQ Management UI → **Queues** → `payment-requested_error`.

Để xử lý lại: trong UI, vào queue `_error` → **Move messages** → nhập tên queue gốc (`payment-requested`).

---

### Checklist trước khi commit

- [ ] Contract (`IntegrationEvent`) nằm trong `Contracts` project, cả publisher và consumer đều reference
- [ ] Handler implement `IIntegrationEventHandler<TEvent>`, không import MassTransit
- [ ] Handler được `AddScoped` trong Application `DependencyInjection.cs`
- [ ] Consumer implement `IConsumer<TEvent>`, chỉ delegate sang handler, không có logic
- [ ] Consumer được `AddConsumer<T>()` trong `AddMassTransitMessaging()`
- [ ] Application DI được gọi trước Infrastructure DI trong `Program.cs`
- [ ] Cập nhật bảng "Danh sách events hiện tại" ở trên

---

## Test end-to-end

### Endpoint test có sẵn

```http
POST https://localhost:8443/async/test/publish
```

Không yêu cầu auth. Publish `TestIntegrationEvent` → `TestConsumer` trong NotificationService nhận → log ra console.

Response `202 Accepted`:
```json
{
  "data": {
    "eventId": "3fa85f64-...",
    "correlationId": "9b3c1a2e-...",
    "message": "TestIntegrationEvent published. Check NotificationService logs."
  }
}
```

Kiểm tra log:
```bash
docker compose logs notification --tail=30 --follow
# Tìm dòng: "Test integrations"
```

### Test thủ công bằng curl

```bash
# 1. Publish event (ví dụ order async)
curl -X POST https://localhost:8443/async/orders \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"items":[{"productName":"Sản phẩm A","quantity":2,"unitPrice":50000}]}' \
  -k

# 2. Xem log OrderService (consumer nhận)
docker compose logs order --tail=30

# 3. Xem log NotificationService (consumer nhận OrderCreated)
docker compose logs notification --tail=30
```

---

## RabbitMQ Management UI

```
URL:      http://localhost:15672
Username: guest
Password: guest
```

| Nơi xem | Ý nghĩa |
|---|---|
| **Exchanges** | `Hdos.Contracts.IntegrationEvents:*` — một exchange fanout per message type |
| **Queues** | `user-logged-in`, `order-created`, `test`, v.v. — một queue per consumer |
| **Queues `*_error`** | Dead-letter: message fail sau 5 lần retry |
| **Queues `*_skipped`** | Message đến queue nhưng không có handler đọc được |

---

## Health Check

MassTransit tự đăng ký khi gọi `AddMassTransitMessaging()`. Không cần cấu hình thêm.

```bash
curl https://localhost:8443/notifications/health/ready -k | jq
```

```json
{
  "status": "Healthy",
  "checks": [
    { "name": "sqlserver", "status": "Healthy" },
    { "name": "masstransit-bus", "status": "Healthy" }
  ]
}
```
