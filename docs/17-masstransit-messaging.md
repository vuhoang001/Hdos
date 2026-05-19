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

Tiếp tục ví dụ trên — giả sử **PaymentService** muốn nhận `PaymentRequestedIntegrationEvent`.

### Bước 1 — Viết Application Handler

Handler nằm trong `Application` layer, **không import MassTransit**:

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

### Bước 2 — Viết Consumer (Infrastructure)

Consumer là adapter mỏng, nằm trong `Infrastructure` layer:

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

> **Quy tắc đặt tên:** Consumer class phải kết thúc bằng `Consumer`. Tên queue = kebab-case của phần trước `Consumer`.  
> `PaymentRequestedConsumer` → queue `payment-requested`.

### Bước 3 — Đăng ký DI

```csharp
// PaymentService.Application/DependencyInjection.cs
services.AddScoped<PaymentRequestedEventHandler>();

// PaymentService.Infrastructure/DependencyInjection.cs
services.AddMassTransitMessaging(configuration, x =>
{
    x.AddConsumer<PaymentRequestedConsumer>();
    // thêm consumer khác nếu có
});
```

**Thứ tự quan trọng:** phải `AddScoped<Handler>()` trong Application DI trước, vì Infrastructure DI gọi Application DI trước khi gọi `AddMassTransitMessaging`.

### Bước 4 — Kiểm tra trên RabbitMQ Management

Sau khi service khởi động, vào `http://localhost:15672`:
- **Exchanges** → tìm `Hdos.Contracts.IntegrationEvents:PaymentRequestedIntegrationEvent` → kiểu fanout
- **Queues** → tìm `payment-requested` → đang bind vào exchange trên

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
