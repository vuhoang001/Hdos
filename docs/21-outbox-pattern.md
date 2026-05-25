# 21 — Transactional Outbox Pattern

Tài liệu này giải thích **tại sao** cần Outbox, **cách hoạt động**, và **cách thêm Outbox cho service mới** trong Hdos. Đọc doc này trước khi thêm bất kỳ `PublishAsync` mới nào vào codebase.

---

## Vấn đề — Dual Write

Mọi command handler publish integration event đều phải thực hiện **2 thao tác ghi độc lập**:

```
1. Ghi vào SQL Server  (business data)
2. Publish lên RabbitMQ (integration event)
```

Hai thao tác này **không có transaction chung**. Nếu service crash hoặc RabbitMQ bị ngắt giữa bước 1 và bước 2, kết quả là:

```
SQL Server: ✅ order đã ghi        ← dữ liệu tồn tại
RabbitMQ:   ❌ event không bao giờ publish ← downstream service không biết
```

Đây gọi là **Dual Write Problem**.

### Khi nào xảy ra trong thực tế

- Deploy service mới trong lúc có request đang xử lý (rolling deploy)
- RabbitMQ restart hoặc network partition ngắn (1–2 giây)
- Unhandled exception sau `SaveChangesAsync` nhưng trước `PublishAsync`
- Out-of-memory / pod eviction

---

## Giải pháp — Transactional Outbox

**Ý tưởng cốt lõi**: thay vì publish thẳng lên RabbitMQ, **ghi event vào 1 bảng trong cùng DB transaction với business data**. Một background worker đọc bảng đó và publish.

```
CommandHandler:
  BEGIN TRANSACTION
    INSERT INTO Orders
    INSERT INTO OutboxMessage  ← cùng transaction
  COMMIT

BusOutboxDeliveryService (background, ~1s interval):
  SELECT * FROM OutboxMessage WHERE DeliveredAt IS NULL
  → Publish to RabbitMQ
  → UPDATE OutboxMessage SET DeliveredAt = NOW()
```

**Đảm bảo**: nếu `COMMIT` thành công → event **chắc chắn** được deliver. Crash trước `COMMIT` → không có gì được ghi. Crash sau `COMMIT` → background worker retry lại sau khi restart.

### Trade-off phải chấp nhận

- **At-least-once delivery**: background worker có thể publish lại nếu crash sau publish nhưng trước khi đánh dấu `DeliveredAt`. Consumer phải **idempotent**.
- **Slight latency**: event đến RabbitMQ sau ~1s thay vì ngay lập tức.
- **3 bảng thêm vào DB**: `OutboxMessage`, `OutboxState`, `InboxState`.

---

## Kiến trúc trong Hdos

Hệ thống dùng **MassTransit EF Core Outbox** kết hợp với **Domain Event → Integration Event** pattern.

### Luồng đầy đủ

```
HTTP Request
    │
    ▼
Command Handler
    ├─ entity.DoAction()          ← entity RaiseDomainEvent()
    └─ uow.SaveChangesAsync()
            │
            ▼
        PublishDomainEventsInterceptor.SavingChangesAsync  ← TRƯỚC khi EF ghi
            ├─ dispatch OrderCreatedDomainEvent via MediatR
            │       │
            │       ▼
            │   OrderCreatedIntegrationEventHandler.Handle()
            │       └─ eventBus.PublishAsync()  ← thêm OutboxMessage vào EF tracker
            │
            └─ (return — không SaveChangesAsync lần 2)
            │
            ▼
        EF Core commit (1 transaction duy nhất)
            ├─ INSERT Orders
            └─ INSERT OutboxMessage        ← cùng transaction, thực sự atomic

        │ (vài trăm ms sau)
        ▼
BusOutboxDeliveryService (IHostedService)
    ├─ SELECT OutboxMessage WHERE DeliveredAt IS NULL
    ├─ Publish to RabbitMQ exchange
    └─ UPDATE OutboxMessage SET DeliveredAt = NOW()

        │
        ▼
Downstream Consumers (NotificationService, etc.)
```

### Tại sao dùng `SavingChangesAsync` (pre-save) thay vì post-save

Interceptor chạy **trước** khi EF ghi. Handler thêm `OutboxMessage` vào EF tracker trong cùng lượt `SaveChangesAsync` → EF commit cả `Orders` lẫn `OutboxMessage` trong **1 transaction duy nhất**.

Nếu dùng `SavedChangesAsync` (post-save): Order commit xong ở transaction 1, sau đó cần transaction 2 cho OutboxMessage — có window crash giữa 2 transaction làm mất event.

---

## Phân loại: 2 pattern publish trong Hdos

### Pattern A — Domain event handler (ưu tiên sử dụng)

Dùng khi integration event chứa **đúng data từ domain event** — không cần truy vấn thêm.

```
Entity.DoAction()           ← raise DomainEvent (contains all needed data)
    │
    ▼
INotificationHandler<TDomainEvent>
    └─ eventBus.PublishAsync(IntegrationEvent)
```

**Ví dụ trong Hdos:**

| Domain Event | Integration Event Handler | Integration Event |
|---|---|---|
| `OrderCreatedDomainEvent` | `OrderCreatedIntegrationEventHandler` | `OrderCreatedIntegrationEvent` |
| `OrderConfirmedDomainEvent` | `OrderConfirmedIntegrationEventHandler` | `OrderConfirmedIntegrationEvent` |

Command handler tương ứng **không có `IEventBus`**:

```csharp
// ✅ CreateOrderCommandHandler — sạch, không publish trực tiếp
public async Task<Result<OrderDto>> Handle(CreateOrderCommand request, CancellationToken ct)
{
    var order = Order.Create(...);  // RaiseDomainEvent bên trong
    await orders.AddAsync(order, ct);
    await uow.SaveChangesAsync(ct);  // → interceptor → handler → OutboxMessage
    return Map(order);
}
```

### Pattern B — Command handler publish trực tiếp (exception)

Dùng khi integration event cần **aggregate data từ DB** không có sẵn trong domain event.

```csharp
// ✅ CreateProductCommandHandler — cần đọc totalPrice/Count sau save
await products.AddAsync(product, ct);
await uow.SaveChangesAsync(ct);              // commit product

var totalPrice = await products.GetTotalPriceAsync(ct);  // đọc aggregate
var totalCount = await products.GetTotalCountAsync(ct);

await eventBus.PublishAsync(new ProductCreatedIntegrationEvent(...), ct);
await uow.SaveChangesAsync(ct);              // commit OutboxMessage
```

**Services dùng Pattern B hiện tại:**

| Service | Handler | Lý do không dùng Pattern A |
|---|---|---|
| `OrderService` | `CreateProductCommandHandler` | `ProductCreatedIntegrationEvent` cần `totalCount`, `totalPrice` từ DB aggregate |
| `M01Service` | `CreateBaoCaoKhoaHandler` | `BaoCaoKhoaCreatedIntegrationEvent` cần `GetAllTimeTotalsAsync` từ DB |

---

## Quy tắc viết Domain Event

Domain event phải chứa **đủ data** để handler downstream tạo được integration event mà không cần query thêm.

### Đúng

```csharp
// OrderCreatedDomainEvent có đủ: email, items list
public sealed record OrderCreatedDomainEvent(
    Guid OrderId,
    Guid CustomerId,
    string CustomerEmail,
    decimal TotalAmount,
    IReadOnlyList<(string ProductName, int Quantity, decimal UnitPrice)> Items
) : DomainEvent;
```

### Sai — thiếu data, handler phải query DB

```csharp
// ❌ Chỉ có OrderId → handler phải query Order từ DB, phức tạp và fragile
public sealed record OrderCreatedDomainEvent(Guid OrderId) : DomainEvent;
```

---

## Quy tắc viết Integration Event Handler (từ Domain Event)

```csharp
// OrderCreatedIntegrationEventHandler.cs
// Vị trí: {Service}.Application/EventHandlers/

public sealed class OrderCreatedIntegrationEventHandler(IEventBus eventBus)
    : INotificationHandler<OrderCreatedDomainEvent>  // ← MediatR tự đăng ký
{
    public async Task Handle(OrderCreatedDomainEvent notification, CancellationToken ct)
    {
        var items = notification.Items
            .Select(i => new OrderItemDto(i.ProductName, i.Quantity, i.UnitPrice))
            .ToList();

        await eventBus.PublishAsync(
            new OrderCreatedIntegrationEvent(
                notification.OrderId,
                notification.CustomerId,
                notification.CustomerEmail,
                notification.TotalAmount,
                items),
            ct);
    }
}
```

**Quy tắc:**
- Implement `INotificationHandler<TDomainEvent>` — MediatR tự scan và đăng ký qua `RegisterServicesFromAssembly`
- Chỉ dùng `IEventBus.PublishAsync` — không bao giờ dùng `IBus.Publish` trực tiếp (bypass outbox)
- Không có logic nghiệp vụ — chỉ map domain event → integration event → publish
- Không inject repository để query thêm — nếu cần, enrichen domain event trước

---

## Hướng dẫn thêm Outbox cho service mới

Ví dụ: thêm Outbox cho `NewService`.

### Bước 1 — Thêm NuGet package

```xml
<!-- NewService.Infrastructure/NewService.Infrastructure.csproj -->
<PackageReference Include="MassTransit.EntityFrameworkCore" Version="8.2.5" />
```

### Bước 2 — Thêm entity config vào DbContext

```csharp
// NewService.Infrastructure/Persistence/NewDbContext.cs
using MassTransit;

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(NewDbContext).Assembly);

    modelBuilder.AddInboxStateEntity();
    modelBuilder.AddOutboxMessageEntity();
    modelBuilder.AddOutboxStateEntity();

    base.OnModelCreating(modelBuilder);
}
```

### Bước 3 — Register trong DependencyInjection.cs

```csharp
using MassTransit;

services.AddMassTransitMessaging(configuration, x =>
{
    x.AddEntityFrameworkOutbox<NewDbContext>(o =>
    {
        o.UseSqlServer();
        o.UseBusOutbox();
    });

    x.AddConsumer<SomeEventConsumer>();
});
```

### Bước 4 — Tách IUnitOfWork riêng (nếu service chưa có)

Xem `M01Service.Domain/Repositories/IM01UnitOfWork.cs` làm mẫu. Repository không nên chứa `SaveChangesAsync`.

### Bước 5 — Tạo migration

```bash
~/.dotnet/dotnet ef migrations add AddOutbox \
  --project src/Services/NewService/NewService.Infrastructure \
  --startup-project src/Services/NewService/NewService.API \
  --output-dir Persistence/Migrations
```

### Bước 6 — Apply migration

```bash
~/.dotnet/dotnet ef database update \
  --project src/Services/NewService/NewService.Infrastructure \
  --startup-project src/Services/NewService/NewService.API
```

---

## Trạng thái hiện tại trong Hdos

| Service | Outbox | Pattern | Integration Events |
|---|---|---|---|
| **OrderService** | ✅ | A (domain event handler) | `OrderCreated`, `OrderConfirmed` |
| **OrderService** | ✅ | B (command handler) | `ProductCreated` (cần aggregate) |
| **M01Service** | ✅ | B (command handler) | `BaoCaoKhoaCreated` (cần aggregate) |
| **NotificationService** | ❌ | — | Chỉ consume, không publish |
| **AuthService** | ❌ | — | Chưa kiểm tra |

---

## Các files liên quan

```
src/BuildingBlocks/Common/
    Persistence/PublishDomainEventsInterceptor.cs  ← dispatch domain events + commit OutboxMessage
    Messaging/IEventBus.cs                         ← abstraction cho publish
    Messaging/MassTransitEventBus.cs               ← implementation dùng IPublishEndpoint
    Extensions/ServiceCollectionExtensions.cs      ← AddMassTransitMessaging

src/Services/OrderService/
    Domain/Events/OrderCreatedDomainEvent.cs       ← chứa đủ data (email, items)
    Domain/Events/OrderConfirmedDomainEvent.cs
    Application/EventHandlers/OrderCreatedIntegrationEventHandler.cs   ← Pattern A
    Application/EventHandlers/OrderConfirmedIntegrationEventHandler.cs ← Pattern A
    Application/Features/CreateOrder/CreateOrderCommand.cs     ← không có IEventBus
    Application/Features/ConfirmOrder/ConfirmOrderCommand.cs   ← không có IEventBus
    Application/Features/Products/CreateProduct/CreateProductCommand.cs ← Pattern B
    Infrastructure/Persistence/OrderDbContext.cs   ← Outbox entity config
    Infrastructure/DependencyInjection.cs          ← AddEntityFrameworkOutbox

src/Services/M01Service/
    Domain/Repositories/IM01UnitOfWork.cs          ← tách riêng khỏi IM01WriteRepository
    Application/Features/BaoCao/CreateBaoCaoKhoaCommand.cs ← Pattern B
    Infrastructure/Persistence/M01WriteRepository.cs + M01UnitOfWork
    Infrastructure/Persistence/M01DbContext.cs     ← Outbox entity config
    Infrastructure/DependencyInjection.cs          ← AddEntityFrameworkOutbox
```

---

## Kiểm tra hoạt động

### Xem bảng OutboxMessage trong DB

```sql
-- Message chờ deliver
SELECT MessageId, MessageType, EnqueueTime
FROM OutboxMessage WHERE DeliveredAt IS NULL
ORDER BY EnqueueTime DESC;

-- Message đã deliver
SELECT MessageId, MessageType, DeliveredAt
FROM OutboxMessage WHERE DeliveredAt IS NOT NULL
ORDER BY DeliveredAt DESC;
```

### Xem log

```bash
docker compose logs order-service --tail=50 | grep -i "outbox\|domain event"
```

---

## Troubleshooting

### OutboxMessage không được deliver

1. RabbitMQ down → worker retry vô hạn, message không mất.
2. `UseBusOutbox()` chưa được gọi trong DI.
3. Service chưa restart sau khi thêm outbox config.

### `InvalidOperationException: requires a primary key`

Migration chưa apply. Chạy `dotnet ef database update`.

### Integration event không được gửi dù domain event đã raise

Kiểm tra:
- `PublishDomainEventsInterceptor` đã được đăng ký trong `DbContext.AddInterceptors()`
- `INotificationHandler` (integration event handler) nằm trong assembly được `RegisterServicesFromAssembly` scan
- `ctx.SaveChangesAsync()` ở cuối interceptor không bị exception

---

## Checklist trước khi commit

**Khi thêm integration event mới (Pattern A):**
- [ ] Domain event đã có đủ data (không cần query thêm)
- [ ] `INotificationHandler<TDomainEvent>` nằm trong `Application/EventHandlers/`
- [ ] Handler chỉ dùng `IEventBus.PublishAsync`, không có logic nghiệp vụ
- [ ] Command handler **không có** `IEventBus` injection

**Khi dùng Pattern B (aggregate data):**
- [ ] Lý do không dùng Pattern A đã được document
- [ ] `PublishAsync` trước `SaveChangesAsync` (nếu không cần read) **hoặc** `SaveChangesAsync` thứ hai sau `PublishAsync`
- [ ] `IUnitOfWork` (không phải `IRepository`) được dùng để save

**Chung:**
- [ ] `MassTransit.EntityFrameworkCore` đã thêm vào Infrastructure `.csproj`
- [ ] `AddInboxStateEntity / AddOutboxMessageEntity / AddOutboxStateEntity` trong `OnModelCreating`
- [ ] `AddEntityFrameworkOutbox<TDbContext>(o => { o.UseSqlServer(); o.UseBusOutbox(); })` trong DI
- [ ] Migration `AddOutbox` đã tạo và apply
- [ ] Cập nhật bảng "Trạng thái hiện tại" ở trên
