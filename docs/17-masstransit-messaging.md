# 17 — MassTransit Messaging, Outbox Pattern & External Consumer

Hệ thống dùng **MassTransit 8.2** làm lớp abstraction trên **RabbitMQ** để truyền integration events giữa các microservices.

---

## Quy tắc đặt tên

| Thành phần | Quy tắc | Ví dụ |
|---|---|---|
| **Integration Event** | `{Tên}IntegrationEvent` | `UserLoggedInIntegrationEvent` |
| **Exchange message-type** | Full namespace, tự động bởi MassTransit | `Hdos.Contracts.IntegrationEvents:UserLoggedInIntegrationEvent` |
| **Consumer** | `{Tên}Consumer` | `UserLoggedInConsumer` |
| **Exchange endpoint + Queue** | Tên consumer, bỏ `Consumer`, kebab-case | `user-logged-in` |
| **Application Handler** | `{Tên}EventHandler` hoặc `{Tên}Handler` | `UserLoggedInEventHandler` |

---

## Topology trong RabbitMQ — tại sao luôn có 2 exchange

MassTransit **luôn tạo 2 exchange** cho mỗi consumer — đây là thiết kế cố ý, không phải lỗi:

```
Publisher
    │
    ▼
Exchange: Hdos.Contracts.IntegrationEvents:UserLoggedInIntegrationEvent [fanout]
    │   ← message-type exchange: route theo loại message
    ▼
Exchange: user-logged-in [fanout]
    │   ← endpoint exchange: route tới consumer cụ thể
    ▼
Queue: user-logged-in ──► UserLoggedInConsumer
```

**Tại sao cần 2 exchange?**

- **Message-type exchange**: Publisher chỉ biết tên event, không cần biết có bao nhiêu consumer. Thêm consumer mới ở service khác → publisher không cần sửa gì.
- **Endpoint exchange**: Mỗi consumer có exchange riêng. Nhiều service cùng subscribe một event mà không ảnh hưởng nhau.

**Ví dụ: 2 service cùng subscribe 1 event:**

```
Exchange: Hdos.Contracts.IntegrationEvents:UserLoggedInIntegrationEvent [fanout]
    ├── Exchange: user-logged-in [fanout]       → Queue: user-logged-in       → NotificationService
    └── Exchange: user-logged-in-audit [fanout] → Queue: user-logged-in-audit → AuditService
```

---

## Cấu hình

### appsettings.json

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

Local dev (`appsettings.Development.json`): đổi `Host` thành `localhost`.

---

## Cách thêm event nội bộ từ đầu đến cuối

Ví dụ: M01Service publish `BaoCaoKhoaCreatedIntegrationEvent`, NotificationService nhận và broadcast SSE.

### Bước 1 — Tạo Integration Event

```csharp
// src/BuildingBlocks/Contracts/IntegrationEvents/BaoCaoKhoaCreatedIntegrationEvent.cs
namespace Hdos.Contracts.IntegrationEvents;

public sealed record BaoCaoKhoaCreatedIntegrationEvent(
    int      TongLuotKham,
    decimal  TongDoanhThu,
    decimal  DoanhThuTrungBinhTheoTuan,
    DateTime NgayBaoCao)
    : IntegrationEvent;
```

`IntegrationEvent` base tự sinh `EventId` (Guid) và `OccurredOnUtc`. Không cần khai báo thêm.

### Bước 2 — Publish từ Command Handler

```csharp
// M01Service.Application/Features/BaoCaoKhoa/CreateBaoCaoKhoaCommand.cs
await eventBus.PublishAsync(new BaoCaoKhoaCreatedIntegrationEvent(
    TongLuotKham:              entity.TongLuotKham,
    TongDoanhThu:              entity.TongDoanhThu,
    DoanhThuTrungBinhTheoTuan: entity.DoanhThuTrungBinhTheoTuan,
    NgayBaoCao:                entity.NgayBaoCao), ct);
```

Publish **sau** `SaveChangesAsync`. Exchange `bao-cao-khoa-created` tự tạo trên RabbitMQ khi message đầu tiên được gửi.

### Bước 3 — Viết Application Handler (phía consumer)

Handler chứa business logic, **không import MassTransit**.

```csharp
// NotificationService.Application/EventHandlers/BaoCaoKhoaCreatedHandler.cs
public sealed class BaoCaoKhoaCreatedHandler(
    INotificationPusher pusher,
    ILogger<BaoCaoKhoaCreatedHandler> logger)
    : IIntegrationEventHandler<BaoCaoKhoaCreatedIntegrationEvent>
{
    public async Task HandleAsync(BaoCaoKhoaCreatedIntegrationEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Broadcasting bao cao khoa for {NgayBaoCao}", @event.NgayBaoCao);

        await pusher.BroadcastEventAsync("bao_cao_khoa_summary",
            new
            {
                tongLuotKham              = @event.TongLuotKham,
                tongDoanhThu              = @event.TongDoanhThu,
                doanhThuTrungBinhTheoTuan = @event.DoanhThuTrungBinhTheoTuan,
                ngayBaoCao                = @event.NgayBaoCao
            }, ct);
    }
}
```

**Quy tắc:**
- Luôn log `LogInformation` khi bắt đầu xử lý
- Gọi `SaveChangesAsync` một lần sau khi xong tất cả DB operations
- **Không bắt exception** — MassTransit retry tự xử lý

### Bước 4 — Viết Consumer (Infrastructure)

Consumer là adapter mỏng nối MassTransit với handler, **không chứa logic**.

```csharp
// NotificationService.Infrastructure/Consumers/BaoCaoKhoaCreatedConsumer.cs
public sealed class BaoCaoKhoaCreatedConsumer(BaoCaoKhoaCreatedHandler handler)
    : IConsumer<BaoCaoKhoaCreatedIntegrationEvent>
{
    public Task Consume(ConsumeContext<BaoCaoKhoaCreatedIntegrationEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
```

### Bước 5 — Đăng ký vào DI

```csharp
// NotificationService.Infrastructure/DependencyInjection.cs
services.AddScoped<BaoCaoKhoaCreatedHandler>();

services.AddMassTransitMessaging(configuration, x =>
{
    x.AddConsumer<BaoCaoKhoaCreatedConsumer>();
    // ...
}, servicePrefix: "notification");
```

### Bước 6 — Kiểm tra trên RabbitMQ

Vào `http://localhost:15672` → phải thấy **2 exchange** per event:

```
Exchanges:
  Hdos.Contracts.IntegrationEvents:BaoCaoKhoaCreatedIntegrationEvent  [fanout]
  bao-cao-khoa-created                                                 [fanout]

Queues:
  notification-bao-cao-khoa-created
```

---

## Tổng hợp các events hiện tại

| Integration Event | Queue | Publisher | Consumer | Handler |
|---|---|---|---|---|
| `UserRegisteredIntegrationEvent` | `notification-user-registered` | AuthService | `UserRegisteredConsumer` | `UserRegisteredEventHandler` |
| `UserLoggedInIntegrationEvent` | `notification-user-logged-in` | AuthService | `UserLoggedInConsumer` | `UserLoggedInEventHandler` |
| `OrderCreateRequestedIntegrationEvent` | `order-order-create-requested` | ApiGateway | `OrderCreateRequestedConsumer` | `OrderCreateRequestedEventHandler` |
| `OrderCreatedIntegrationEvent` | `notification-order-created` | OrderService | `OrderCreatedConsumer` | `OrderCreatedEventHandler` |
| `OrderConfirmedIntegrationEvent` | `notification-order-confirmed` | OrderService | `OrderConfirmedConsumer` | `OrderConfirmedEventHandler` |
| `NotificationSendRequestedIntegrationEvent` | `notification-notification-send-requested` | ApiGateway | `NotificationSendRequestedConsumer` | `NotificationSendRequestedEventHandler` |
| `ProductCreatedIntegrationEvent` | `notification-product-created` | OrderService | `ProductCreatedConsumer` | `ProductCreatedEventHandler` |
| `ProductCreatedIntegrationEvent` | `notification-product-total-updated` | OrderService | `ProductTotalUpdatedConsumer` | `ProductTotalUpdatedHandler` |
| `BaoCaoKhoaCreatedIntegrationEvent` | `notification-bao-cao-khoa-created` | M01Service | `BaoCaoKhoaCreatedConsumer` | `BaoCaoKhoaCreatedHandler` |
| `DashboardFeReadyIntegrationEvent` | `notification-dashboard-fe-ready` | DataMatchingService | `DashboardFeReadyConsumer` | `DashboardFeReadyHandler` |

**External consumers** (nhận từ hệ thống bên ngoài — xem [External Consumer Pattern](#external-consumer-pattern)):

| Consumer | Queue | Source |
|---|---|---|
| `ProcessedToFeConsumer` | `be.hdos.dashboard.fe.ready` | Third-party system |

---

## Dead-letter & Retry

### Flow khi handler throw exception

```
Handler throw exception
    ├─ Retry lần 1: chờ ~1s
    ├─ Retry lần 2: chờ ~6s
    ├─ Retry lần 3: chờ ~11s
    ├─ Retry lần 4: chờ ~16s
    ├─ Retry lần 5: chờ ~21s
    └─ Hết retry → message chuyển sang: {queue}_error
```

### Re-process message lỗi

1. Vào `http://localhost:15672` → **Queues** → `{queue}_error`
2. **Get messages** để xem nội dung và exception
3. **Move messages** → nhập queue gốc để retry lại

---

## Test End-to-End

### 1. Lấy JWT token

```bash
TOKEN=$(curl -sk https://localhost:8443/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"testuser@hdos.dev","password":"Test1234!"}' \
  | jq -r '.data.token')
```

### 2. Publish OrderCreateRequested

```bash
curl -s -X POST https://localhost:8443/async/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"items":[{"productName":"Demo Product","quantity":2,"unitPrice":50000}]}' \
  -k | jq .
```

### 3. Kiểm tra consumer nhận message

```bash
docker compose logs orderservice --tail=20
docker compose logs notificationservice --tail=20
```

### 4. Verify trên RabbitMQ Management

`http://localhost:15672` → **Queues** → xem các queue có `Ready = 0` (đã consume hết).

### Troubleshooting

```bash
# Consumer đang listen không?
curl -s -u guest:guest http://localhost:15672/api/queues/%2F/notification-order-created | jq '.consumer_count'

# Có message lỗi không?
curl -s -u guest:guest http://localhost:15672/api/queues/%2F/notification-order-created_error | jq '.messages'
```

---

## Trường hợp ngoại lệ: Consumer tên khác event

Khi consumer không theo đúng quy ước đặt tên (VD: `ProductTotalUpdatedConsumer` nhưng consume `ProductCreatedIntegrationEvent`), RabbitMQ tạo **2 exchange riêng biệt**:

```
Exchange: product-created [fanout]       ← message-type exchange
    └── Exchange: notification-product-total-updated [fanout]   ← endpoint exchange
            └── Queue: notification-product-total-updated
```

Vẫn hoạt động đúng. Tránh trường hợp này trừ khi có lý do rõ ràng.

---

## Dọn dẹp exchange cũ

Exchange durable — không tự xóa khi restart. Cần xóa thủ công qua `http://localhost:15672` → Exchanges → xóa exchange không còn dùng.

---

## Cấu hình nội bộ (ServiceCollectionExtensions)

```csharp
services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();
    configure?.Invoke(x);

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(hostUri, h => { h.Username(...); h.Password(...); });

        cfg.UseMessageRetry(r => r.Exponential(
            retryLimit: 5, minInterval: 1s, maxInterval: 30s, intervalDelta: 5s));

        cfg.ConfigureEndpoints(ctx);
    });
});
```

---

## Transactional Outbox Pattern

Đọc section này trước khi thêm bất kỳ `PublishAsync` mới nào vào codebase.

### Vấn đề — Dual Write

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

Đây gọi là **Dual Write Problem**. Xảy ra trong thực tế khi: rolling deploy, RabbitMQ restart ngắn (~1-2s), unhandled exception sau `SaveChangesAsync`, OOM/pod eviction.

### Giải pháp — Transactional Outbox

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

**Trade-off phải chấp nhận:**
- **At-least-once delivery**: background worker có thể publish lại. Consumer phải **idempotent**.
- **Slight latency**: event đến RabbitMQ sau ~1s thay vì ngay lập tức.
- **3 bảng thêm vào DB**: `OutboxMessage`, `OutboxState`, `InboxState`.

### Kiến trúc trong Hdos

Hệ thống dùng **MassTransit EF Core Outbox** kết hợp với **Domain Event → Integration Event** pattern.

**Luồng đầy đủ:**

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
```

**Tại sao dùng `SavingChangesAsync` (pre-save)?** Interceptor chạy **trước** khi EF ghi. Handler thêm `OutboxMessage` vào EF tracker trong cùng lượt `SaveChangesAsync` → EF commit cả `Orders` lẫn `OutboxMessage` trong **1 transaction duy nhất**.

### 2 pattern publish trong Hdos

#### Pattern A — Domain event handler (ưu tiên)

Dùng khi integration event chứa **đúng data từ domain event** — không cần truy vấn thêm.

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

| Domain Event | Integration Event Handler | Integration Event |
|---|---|---|
| `OrderCreatedDomainEvent` | `OrderCreatedIntegrationEventHandler` | `OrderCreatedIntegrationEvent` |
| `OrderConfirmedDomainEvent` | `OrderConfirmedIntegrationEventHandler` | `OrderConfirmedIntegrationEvent` |

#### Pattern B — Command handler publish trực tiếp (exception)

Dùng khi integration event cần **aggregate data từ DB** không có sẵn trong domain event.

```csharp
// ✅ CreateProductCommandHandler — cần đọc totalPrice/Count sau save
await products.AddAsync(product, ct);
await uow.SaveChangesAsync(ct);              // commit product

var totalPrice = await products.GetTotalPriceAsync(ct);
var totalCount = await products.GetTotalCountAsync(ct);

await eventBus.PublishAsync(new ProductCreatedIntegrationEvent(...), ct);
await uow.SaveChangesAsync(ct);              // commit OutboxMessage
```

| Service | Handler | Lý do không dùng Pattern A |
|---|---|---|
| `OrderService` | `CreateProductCommandHandler` | `ProductCreatedIntegrationEvent` cần `totalCount`, `totalPrice` từ DB aggregate |
| `M01Service` | `CreateBaoCaoKhoaHandler` | `BaoCaoKhoaCreatedIntegrationEvent` cần `GetAllTimeTotalsAsync` từ DB |

### Quy tắc viết Domain Event

Domain event phải chứa **đủ data** để handler downstream tạo được integration event mà không cần query thêm.

```csharp
// ✅ Đúng — có đủ data
public sealed record OrderCreatedDomainEvent(
    Guid OrderId, Guid CustomerId, string CustomerEmail,
    decimal TotalAmount,
    IReadOnlyList<(string ProductName, int Quantity, decimal UnitPrice)> Items
) : DomainEvent;

// ❌ Sai — handler phải query DB
public sealed record OrderCreatedDomainEvent(Guid OrderId) : DomainEvent;
```

### Hướng dẫn thêm Outbox cho service mới

**Bước 1** — Thêm NuGet package:

```xml
<PackageReference Include="MassTransit.EntityFrameworkCore" Version="8.2.5" />
```

**Bước 2** — Thêm entity config vào DbContext:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(NewDbContext).Assembly);
    modelBuilder.AddInboxStateEntity();
    modelBuilder.AddOutboxMessageEntity();
    modelBuilder.AddOutboxStateEntity();
    base.OnModelCreating(modelBuilder);
}
```

**Bước 3** — Register trong DependencyInjection.cs:

```csharp
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

**Bước 4** — Tạo migration:

```bash
dotnet ef migrations add AddOutbox \
  --project src/Services/NewService/NewService.Infrastructure \
  --startup-project src/Services/NewService/NewService.API
```

### Trạng thái hiện tại trong Hdos

| Service | Outbox | Pattern | Integration Events |
|---|---|---|---|
| **OrderService** | ✅ | A (domain event handler) | `OrderCreated`, `OrderConfirmed` |
| **OrderService** | ✅ | B (command handler) | `ProductCreated` (cần aggregate) |
| **M01Service** | ✅ | B (command handler) | `BaoCaoKhoaCreated` (cần aggregate) |
| **NotificationService** | ❌ | — | Chỉ consume, không publish |
| **AuthService** | ❌ | — | Chưa kiểm tra |

### Kiểm tra hoạt động

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

```bash
docker compose logs order-service --tail=50 | grep -i "outbox\|domain event"
```

### Troubleshooting Outbox

| Triệu chứng | Fix |
|---|---|
| OutboxMessage không được deliver | Kiểm tra `UseBusOutbox()` trong DI; RabbitMQ down (worker retry tự động) |
| `InvalidOperationException: requires a primary key` | Migration chưa apply |
| Integration event không gửi dù domain event raised | Kiểm tra `PublishDomainEventsInterceptor` đã đăng ký; `INotificationHandler` nằm trong assembly được scan |

---

## External Consumer Pattern

Pattern để nhận messages từ **hệ thống bên ngoài không dùng MassTransit envelope format** qua RabbitMQ, với mỗi consumer hoàn toàn độc lập và khai báo đơn giản bằng attribute.

> Toàn bộ pattern vẫn chạy trên MassTransit — `IConsumer<T>`, `ReceiveEndpoint`, retry, dead-letter đều nguyên vẹn. Điểm khác biệt duy nhất là dùng `UseRawJsonDeserializer` để bỏ qua MassTransit envelope mà bên ngoài không gửi.

### Vấn đề cần giải quyết

- Bên ngoài gửi JSON thuần, không có MassTransit envelope (`messageType`, `messageId`…)
- Tên queue/exchange do bên ngoài quy định, không theo convention nội bộ
- Mỗi source system có thể cần prefetch count và concurrency khác nhau
- Số lượng source tăng dần → không muốn sửa file DI mỗi lần thêm mới

### Giải pháp: `[ExternalConsumer]` Attribute

Đặt attribute lên consumer class → system tự scan, tự tạo queue, tự wire up. Mỗi consumer hoàn toàn độc lập.

```csharp
[ExternalConsumer("tên-queue")]                                         // mặc định
[ExternalConsumer("tên-queue", UseRawJson = false)]                    // bên ngoài gửi MassTransit envelope
[ExternalConsumer("tên-queue", PrefetchCount = 50, ConcurrentLimit = 10)]
```

| Property | Mặc định | Ý nghĩa |
|---|---|---|
| `QueueName` | *(bắt buộc)* | Tên queue trên RabbitMQ |
| `UseRawJson` | `true` | Bật raw JSON deserializer |
| `PrefetchCount` | `10` | Số messages prefetch từ broker |
| `ConcurrentLimit` | `5` | Số messages xử lý đồng thời |

Attribute này kế thừa `ExcludeFromConfigureEndpointsAttribute` của MassTransit — `cfg.ConfigureEndpoints()` sẽ **bỏ qua** consumer này, tránh tạo endpoint trùng.

### Cách thêm consumer mới

**Bước 1** — Tạo message contract:

```csharp
// Application/DTOs/LabResultMessage.cs
public sealed record LabResultMessage(
    string? PatientId, string? TestCode, string? Result, DateTime? TestedAt);
```

**Bước 2** — Tạo handler trong Application layer:

```csharp
// Application/EventHandlers/LabResultHandler.cs
public sealed class LabResultHandler(INotificationPusher pusher, ILogger<LabResultHandler> logger)
{
    public async Task HandleAsync(LabResultMessage message, CancellationToken ct)
    {
        logger.LogInformation("Lab result received for patient {PatientId}", message.PatientId);
        await pusher.BroadcastEventAsync("lab-result",
            new { patientId = message.PatientId, testCode = message.TestCode, result = message.Result }, ct);
    }
}
```

**Bước 3** — Tạo consumer với `[ExternalConsumer]`:

```csharp
// Infrastructure/Consumers/LabResultConsumer.cs
[ExternalConsumer("external.lab-result", PrefetchCount = 20, ConcurrentLimit = 8)]
public sealed class LabResultConsumer(LabResultHandler handler)
    : IConsumer<LabResultMessage>
{
    public Task Consume(ConsumeContext<LabResultMessage> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
```

**Xong.** Không cần sửa bất kỳ file nào khác.

### Setup một lần trong DependencyInjection.cs

```csharp
services.AddMassTransitMessaging(configuration, x =>
{
    // Internal consumers — đăng ký thủ công
    x.AddConsumer<UserLoggedInConsumer>();
    x.AddConsumer<OrderCreatedConsumer>();
    // External consumers KHÔNG đăng ký ở đây — [ExternalConsumer] tự xử lý
},
servicePrefix: "notification",
externalConsumersAssembly: typeof(DependencyInjection).Assembly);  // ← scan assembly này
```

### Cơ chế hoạt động (lúc startup)

```
AddMassTransitMessaging()
    │
    ├── configure?.Invoke(x)           ← internal consumers đăng ký thủ công
    ├── x.AddExternalConsumers(asm)    ← scan [ExternalConsumer], đăng ký vào DI
    └── UsingRabbitMq(...)
             ├── cfg.ConfigureExternalEndpoints(ctx, asm)
             │        └── per found type:
             │             ├── cfg.ReceiveEndpoint(attr.QueueName, ...)
             │             ├── e.UseRawJsonDeserializer()  [nếu UseRawJson = true]
             │             └── e.ConfigureConsumer(ctx, type)
             └── cfg.ConfigureEndpoints(ctx)   ← bỏ qua [ExternalConsumer] classes
```

### Consumers hiện có

| Consumer | Queue | Source | PrefetchCount |
|---|---|---|---|
| `ProcessedToFeConsumer` | `be.hdos.dashboard.fe.ready` | DataMatchingService | 10 (mặc định) |

### Troubleshooting External Consumer

```bash
# Tìm tất cả external consumers trong codebase
grep -r "\[ExternalConsumer" src/ --include="*.cs"
```

| Triệu chứng | Fix |
|---|---|
| Consumer không nhận được message | Kiểm tra tên queue trong attribute; kiểm tra binding exchange ↔ queue trên RabbitMQ Management |
| Deserialization lỗi | Kiểm tra field names trong record khớp với JSON; dùng `object?` cho field có format thay đổi |

---

## Checklist trước khi commit

**Khi thêm integration event mới (Pattern A):**
- [ ] Domain event đã có đủ data (không cần query thêm)
- [ ] `INotificationHandler<TDomainEvent>` nằm trong `Application/EventHandlers/`
- [ ] Handler chỉ dùng `IEventBus.PublishAsync`, không có logic nghiệp vụ
- [ ] Command handler **không có** `IEventBus` injection

**Khi dùng Pattern B (aggregate data):**
- [ ] Lý do không dùng Pattern A đã được document
- [ ] `IUnitOfWork` (không phải `IRepository`) được dùng để save

**Outbox (mọi service publish event):**
- [ ] `MassTransit.EntityFrameworkCore` đã thêm vào Infrastructure `.csproj`
- [ ] `AddInboxStateEntity / AddOutboxMessageEntity / AddOutboxStateEntity` trong `OnModelCreating`
- [ ] `AddEntityFrameworkOutbox<TDbContext>(o => { o.UseSqlServer(); o.UseBusOutbox(); })` trong DI
- [ ] Migration `AddOutbox` đã tạo và apply
- [ ] Cập nhật bảng "Trạng thái hiện tại" ở trên

**Chung:**
- [ ] Event nằm trong `Contracts` project, record kế thừa `IntegrationEvent`
- [ ] Tên consumer = `{EventName bỏ IntegrationEvent}Consumer` (để exchange merge về 1)
- [ ] Handler implement `IIntegrationEventHandler<TEvent>`, không import MassTransit
- [ ] Consumer implement `IConsumer<TEvent>`, chỉ delegate sang handler, không có logic
- [ ] Consumer được `AddConsumer<T>()` hoặc `[ExternalConsumer]` attribute
- [ ] Handler được `AddScoped` trong Infrastructure `DependencyInjection.cs`
- [ ] Cập nhật bảng "Tổng hợp các events hiện tại" ở trên
