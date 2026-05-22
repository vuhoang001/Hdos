# 17 — MassTransit Messaging

Hệ thống dùng **MassTransit 8.2** làm lớp abstraction trên **RabbitMQ** để truyền integration events giữa các microservices. MassTransit quản lý toàn bộ vòng đời của consumer, retry, dead-letter và health check.

---

## Cấu hình

### appsettings.json

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

### Extension method

`AddMassTransitMessaging` nằm trong `Common/Extensions/ServiceCollectionExtensions.cs`:

```csharp
// Service chỉ publish, không consume (AuthService, M01Service, ApiGateway)
services.AddMassTransitMessaging(configuration);

// Service vừa publish vừa consume (NotificationService, OrderService)
services.AddMassTransitMessaging(configuration, x =>
{
    x.AddConsumer<UserLoggedInConsumer>();
    x.AddConsumer<OrderCreatedConsumer>();
    // thêm consumer khác...
});
```

Extension này tự động:
- Kết nối RabbitMQ theo config
- Đặt tên queue theo kebab-case (vd. `UserLoggedInConsumer` → queue `user-logged-in`)
- Đặt tên exchange theo kebab-case, bỏ suffix `IntegrationEvent` (vd. `UserLoggedInIntegrationEvent` → exchange `user-logged-in`)
- Retry exponential backoff: tối đa 5 lần, từ 1s đến 30s
- Đăng ký `IEventBus` và health check vào DI

---

## Topology RabbitMQ

### Exchange đơn — quy tắc cốt lõi

MassTransit mặc định tạo **2 exchange** cho mỗi consumer:
1. **Message-type exchange** — publisher publish vào đây (đặt tên theo `IEntityNameFormatter`)
2. **Endpoint exchange** — cùng tên queue, queue bind vào đây

Khi đặt tên nhất quán, cả hai exchange có **cùng tên** và RabbitMQ coi chúng là **1 exchange duy nhất**:

```
Exchange: user-logged-in [fanout]
     └── Queue: user-logged-in → UserLoggedInConsumer
```

Để điều này xảy ra, tên consumer phải theo quy ước `{EventNameBỏSuffix}Consumer`:

| Integration Event | Exchange (message-type) | Consumer | Endpoint exchange | Merge? |
|---|---|---|---|---|
| `UserLoggedInIntegrationEvent` | `user-logged-in` | `UserLoggedInConsumer` | `user-logged-in` | ✅ 1 exchange |
| `OrderCreatedIntegrationEvent` | `order-created` | `OrderCreatedConsumer` | `order-created` | ✅ 1 exchange |
| `ProductCreatedIntegrationEvent` | `product-created` | `ProductTotalUpdatedConsumer` | `product-total-updated` | ❌ 2 exchange |

Khi tên không khớp (dòng cuối), hai exchange vẫn được **bind với nhau** và hoạt động đúng — chỉ là tạo thêm 1 exchange trong RabbitMQ.

### Nhiều consumer cùng subscribe 1 event

Mỗi consumer nhận **bản sao riêng** của message (fanout):

```
Exchange: order-created [fanout]
     ├── Exchange: order-created → Queue: order-created → OrderCreatedConsumer (NotificationService)
     └── Exchange: order-created-audit → Queue: order-created-audit → OrderCreatedAuditConsumer (AuditService)
```

Hai service khác nhau subscribe cùng event → mỗi service có queue riêng → cả hai đều nhận đủ message.

> **Lưu ý:** Nếu hai service có consumer class **cùng tên** (ví dụ cả hai đều có `OrderCreatedConsumer`), chúng sẽ dùng chung queue `order-created` và **phân chia** message thay vì cả hai cùng nhận. Cần thêm prefix service vào endpoint formatter trong trường hợp đó.

### Danh sách exchanges hiện tại

| Exchange (kebab-case) | Message type | Consumer | Service |
|---|---|---|---|
| `user-registered` | `UserRegisteredIntegrationEvent` | `UserRegisteredConsumer` | NotificationService |
| `user-logged-in` | `UserLoggedInIntegrationEvent` | `UserLoggedInConsumer` | NotificationService |
| `order-create-requested` | `OrderCreateRequestedIntegrationEvent` | `OrderCreateRequestedConsumer` | OrderService |
| `order-created` | `OrderCreatedIntegrationEvent` | `OrderCreatedConsumer` | NotificationService |
| `order-confirmed` | `OrderConfirmedIntegrationEvent` | `OrderConfirmedConsumer` | NotificationService |
| `notification-send-requested` | `NotificationSendRequestedIntegrationEvent` | `NotificationSendRequestedConsumer` | NotificationService |
| `product-created` | `ProductCreatedIntegrationEvent` | `ProductCreatedConsumer` | NotificationService |
| `product-created` → `product-total-updated` | `ProductCreatedIntegrationEvent` | `ProductTotalUpdatedConsumer` | NotificationService |
| `bao-cao-khoa-created` | `BaoCaoKhoaCreatedIntegrationEvent` | `BaoCaoKhoaCreatedConsumer` | NotificationService |
| `test` | `TestIntegrationEvent` | `TestConsumer` | NotificationService |
| `hoanggggf` | `HoanggggfIntegrationEvent` | `HoanggggfConsumer` | NotificationService |
| `hoanggggf` → `hoanggggf-error` | `HoanggggfIntegrationEvent` | `HoanggggfErrorConsumer` | NotificationService |

---

## Cách hoạt động

### Publish

```
Service gọi IEventBus.PublishAsync(event)
    └─ MassTransitEventBus
         └─ IPublishEndpoint.Publish(event, runtimeType)
              └─ Exchange: user-logged-in [fanout]
                   └─ Queue: user-logged-in → consumer nhận
```

### Consume

```
Message vào queue user-logged-in
    └─ MassTransit tạo scope DI mới
         └─ Resolve UserLoggedInConsumer
              └─ Gọi UserLoggedInEventHandler.HandleAsync()
                   ├─ Thành công → Ack, xóa khỏi queue
                   └─ Exception  → Retry (tối đa 5 lần, exponential backoff)
                                    └─ Vẫn fail → queue user-logged-in_error
```

Consumer trong Infrastructure là **adapter mỏng** — chỉ nhận message và delegate xuống Application handler. Mọi business logic nằm trong Application handler.

---

## Hướng dẫn thêm publisher mới

Giả sử **OrderService** muốn bắn sự kiện `PaymentRequestedIntegrationEvent`.

### Bước 1 — Định nghĩa contract

Thêm file trong project `Contracts` (tất cả services đều reference):

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

`IntegrationEvent` là base record, tự sinh `EventId` (Guid) và `OccurredOnUtc` (DateTime).

### Bước 2 — Inject `IEventBus` và publish

```csharp
// OrderService.Application/Features/CreateOrder/CreateOrderCommandHandler.cs

public sealed class CreateOrderCommandHandler(
    IOrderRepository repo,
    IUnitOfWork uow,
    IEventBus eventBus,
    ILogger<CreateOrderCommandHandler> logger) : IRequestHandler<...>
{
    public async Task<Result<OrderDto>> Handle(CreateOrderCommand command, CancellationToken ct)
    {
        var order = Order.Create(command.CustomerId, command.Items);
        await repo.AddAsync(order, ct);
        await uow.SaveChangesAsync(ct);

        await eventBus.PublishAsync(new PaymentRequestedIntegrationEvent(
            OrderId:    order.Id,
            CustomerId: order.CustomerId,
            Amount:     order.TotalAmount,
            Currency:   "VND"), ct);

        return Result.Success(order.ToDto());
    }
}
```

MassTransit tự tạo exchange `payment-requested` trên RabbitMQ khi message đầu tiên được gửi.

---

## Hướng dẫn thêm consumer mới

### Kiến trúc 2 tầng

```
Infrastructure                    Application
──────────────────────────────    ─────────────────────────────────
PaymentRequestedConsumer          PaymentRequestedEventHandler
  IConsumer<TEvent>  ──────────►    IIntegrationEventHandler<TEvent>
  thin adapter                       business logic, không import MassTransit
```

Consumer chỉ là dây nối. Handler chứa logic, không phụ thuộc framework → dễ unit test.

---

Ví dụ: **PaymentService** subscribe `PaymentRequestedIntegrationEvent`.

### Bước 1 — Kiểm tra contract

Nếu event chưa có trong `Contracts`, tạo mới (xem mục "Thêm publisher"). Nếu đã có, bỏ qua.

### Bước 2 — Viết Application Handler

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
        logger.LogInformation("Processing payment for Order {OrderId}", @event.OrderId);

        var payment = Payment.Create(@event.OrderId, @event.Amount, @event.Currency);
        await repo.AddAsync(payment, ct);
        await uow.SaveChangesAsync(ct);
    }
}
```

**Quy tắc:**
- Inject dependency qua constructor, không qua `IServiceProvider`
- Log ít nhất một `LogInformation` khi bắt đầu xử lý
- Gọi `SaveChangesAsync` một lần sau khi xong tất cả DB operations
- Không bắt exception — MassTransit retry policy tự xử lý

### Bước 3 — Đăng ký Handler vào Application DI

```csharp
// PaymentService.Application/DependencyInjection.cs
public static IServiceCollection AddPaymentApplication(this IServiceCollection services)
{
    services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));
    services.AddCommonMediatRBehaviors();

    services.AddScoped<PaymentRequestedEventHandler>();  // ← thêm dòng này

    return services;
}
```

Dùng `AddScoped` vì handler inject repository (scoped) và DbContext (scoped).

### Bước 4 — Viết Consumer (Infrastructure)

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

Consumer không chứa bất kỳ logic nào — chỉ nhận và delegate.

### Bước 5 — Đăng ký Consumer vào Infrastructure DI

```csharp
// PaymentService.Infrastructure/DependencyInjection.cs
services.AddMassTransitMessaging(configuration, x =>
{
    x.AddConsumer<PaymentRequestedConsumer>();  // ← thêm dòng này
});
```

### Bước 6 — Kiểm tra thứ tự DI trong Program.cs

```csharp
builder.Services.AddPaymentApplication();      // 1. Application trước
builder.Services.AddPaymentInfrastructure();   // 2. Infrastructure sau
```

Nếu ngược lại: service khởi động được nhưng throw `InvalidOperationException` khi nhận message đầu tiên.

### Bước 7 — Kiểm tra trên RabbitMQ Management

Sau khi service khởi động, vào `http://localhost:15672`:
- **Exchanges** → tìm `payment-requested` (fanout)
- **Queues** → tìm `payment-requested` → đang bind vào exchange trên

---

## Quy tắc đặt tên

### Exchange và Queue

Exchange được đặt tên từ **tên event, bỏ suffix `IntegrationEvent`**, chuyển sang kebab-case:

| Integration Event | Exchange |
|---|---|
| `UserLoggedInIntegrationEvent` | `user-logged-in` |
| `OrderCreateRequestedIntegrationEvent` | `order-create-requested` |
| `BaoCaoKhoaCreatedIntegrationEvent` | `bao-cao-khoa-created` |
| `PaymentRequestedIntegrationEvent` | `payment-requested` |

Queue được đặt tên từ **tên consumer, bỏ suffix `Consumer`**, chuyển sang kebab-case:

| Consumer | Queue |
|---|---|
| `UserLoggedInConsumer` | `user-logged-in` |
| `OrderCreateRequestedConsumer` | `order-create-requested` |
| `PaymentRequestedConsumer` | `payment-requested` |

### Quy ước đặt tên Consumer để exchange merge về 1

Để exchange và queue có **cùng tên** (merge thành 1 entity trong RabbitMQ):

```
Consumer class = {EventName bỏ "IntegrationEvent"}Consumer
```

Ví dụ:
```
UserLoggedInIntegrationEvent  →  UserLoggedInConsumer      ✅ merge
OrderCreatedIntegrationEvent  →  OrderCreatedConsumer       ✅ merge
ProductCreatedIntegrationEvent → ProductTotalUpdatedConsumer ❌ không merge (vẫn hoạt động, nhưng tạo 2 exchange)
```

---

## Dead-letter & Retry

### Khi handler throw exception

```
Handler throw exception
    └─ MassTransit retry: exponential backoff
         Lần 1: chờ ~1s
         Lần 2: chờ ~6s
         Lần 3: chờ ~11s
         Lần 4: chờ ~16s
         Lần 5: chờ ~21s
         └─ Vẫn fail sau 5 lần → message chuyển sang queue: payment-requested_error
```

### Xem và xử lý message lỗi

1. Vào RabbitMQ Management UI → **Queues** → `payment-requested_error`
2. Xem nội dung message và exception header
3. Để retry lại: **Get messages** → xem lỗi → **Move messages** → nhập queue gốc (`payment-requested`)

---

## Dọn dẹp exchange cũ sau khi đổi naming convention

Khi đổi sang naming mới (kebab-case), các exchange cũ dạng `Hdos.Contracts.IntegrationEvents:*` vẫn tồn tại trong RabbitMQ vì chúng là **durable** — không tự xóa khi service restart.

**Cách xóa:**
1. Vào `http://localhost:15672` → **Exchanges**
2. Tìm tất cả exchange có prefix `Hdos.Contracts.IntegrationEvents:`
3. Vào từng exchange → **Delete**

Sau khi xóa, các exchange này sẽ không được tạo lại vì tất cả services đã dùng formatter mới.

> **Lưu ý:** Chỉ xóa khi chắc chắn không còn service nào đang publish/subscribe vào exchange đó.

---

## RabbitMQ Management UI

```
URL:      http://localhost:15672
Username: guest
Password: guest
```

| Tab | Nơi xem | Ý nghĩa |
|---|---|---|
| Exchanges | `user-logged-in`, `order-created`, ... | Một fanout exchange per message type (đã merge với endpoint exchange) |
| Queues | `user-logged-in`, `order-created`, ... | Một queue per consumer |
| Queues `*_error` | `user-logged-in_error` | Dead-letter: message fail sau 5 lần retry |
| Queues `*_skipped` | `user-logged-in_skipped` | Message đến queue nhưng không có handler đọc được |

---

## Test end-to-end

### Endpoint test có sẵn

```http
POST https://localhost:8443/async/test/publish
```

Không yêu cầu auth. Publish `TestIntegrationEvent` → `TestConsumer` trong NotificationService nhận → log ra console.

```bash
docker compose logs notification --tail=30 --follow
# Tìm dòng chứa "Test integrations"
```

### Test thủ công

```bash
# Publish event
curl -X POST https://localhost:8443/async/orders \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"items":[{"productName":"Sản phẩm A","quantity":2,"unitPrice":50000}]}' \
  -k

# Xem log OrderService (consumer nhận OrderCreateRequested)
docker compose logs order --tail=30

# Xem log NotificationService (consumer nhận OrderCreated)
docker compose logs notification --tail=30
```

---

## Health Check

```bash
curl https://localhost:8443/notifications/health/ready -k | jq
```

```json
{
  "status": "Healthy",
  "checks": [
    { "name": "sqlserver",       "status": "Healthy" },
    { "name": "masstransit-bus", "status": "Healthy" }
  ]
}
```

---

## Checklist trước khi commit

- [ ] Contract nằm trong `Contracts` project, cả publisher và consumer đều reference
- [ ] Tên consumer theo quy ước `{EventName bỏ IntegrationEvent}Consumer` để exchange merge về 1
- [ ] Handler implement `IIntegrationEventHandler<TEvent>`, không import MassTransit
- [ ] Handler được `AddScoped` trong Application `DependencyInjection.cs`
- [ ] Consumer implement `IConsumer<TEvent>`, chỉ delegate sang handler, không có logic
- [ ] Consumer được `AddConsumer<T>()` trong `AddMassTransitMessaging()`
- [ ] Application DI được gọi trước Infrastructure DI trong `Program.cs`
- [ ] Cập nhật bảng "Danh sách exchanges hiện tại" ở trên
