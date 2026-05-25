# 21 — Transactional Outbox Pattern

Tài liệu này giải thích **tại sao** cần Outbox, **cách hoạt động**, và **cách thêm Outbox cho service mới** trong Hdos. Đọc doc này trước khi thêm bất kỳ `PublishAsync` mới nào vào command handler.

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

Đây gọi là **Dual Write Problem** — không có distributed transaction 2PC giữa SQL Server và RabbitMQ. Mọi kiến trúc microservices đều phải đối mặt với vấn đề này.

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
  BEGIN TRANSACTION (ngầm, EF quản lý)
    INSERT INTO Orders   (business data)
    INSERT INTO OutboxMessage  (event payload)
  COMMIT ← atomic: hoặc cả 2, hoặc không cái nào

BusOutboxDeliveryService (background, chạy mỗi ~1s):
  SELECT * FROM OutboxMessage WHERE DeliveredAt IS NULL
  → Publish to RabbitMQ
  → UPDATE OutboxMessage SET DeliveredAt = NOW()
```

**Đảm bảo**: nếu `COMMIT` thành công → event **chắc chắn** được deliver. Nếu crash trước `COMMIT` → không ghi gì cả. Nếu crash sau `COMMIT` nhưng trước khi RabbitMQ nhận → background worker retry lại khi service restart.

### Trade-off phải chấp nhận

- **At-least-once delivery**: background worker có thể publish cùng 1 message 2 lần nếu crash ngay sau publish nhưng trước khi đánh dấu delivered. Consumer **phải idempotent**.
- **Slight latency**: event đến RabbitMQ sau ~1s thay vì ngay lập tức. Chấp nhận được với async flow.
- **3 bảng thêm vào DB**: `OutboxMessage`, `OutboxState`, `InboxState`.

---

## Kiến trúc trong Hdos

Hệ thống dùng **MassTransit EF Core Outbox** — không tự cài đặt background worker, MassTransit làm hết.

```
src/BuildingBlocks/Common/
    Extensions/ServiceCollectionExtensions.cs  ← AddMassTransitMessaging

src/Services/OrderService/
    Infrastructure/DependencyInjection.cs       ← AddEntityFrameworkOutbox<OrderDbContext>
    Infrastructure/Persistence/OrderDbContext.cs ← 3 bảng outbox entity config

src/Services/M01Service/
    Infrastructure/DependencyInjection.cs       ← AddEntityFrameworkOutbox<M01DbContext>
    Infrastructure/Persistence/M01DbContext.cs  ← 3 bảng outbox entity config
```

### Các bảng sinh ra sau migration

| Bảng | Vai trò |
|---|---|
| `OutboxMessage` | Lưu mỗi integration event chờ deliver |
| `OutboxState` | Trạng thái của outbox delivery session (lock khi deliver) |
| `InboxState` | Idempotency key: track message đã được consumer xử lý chưa |

---

## Luồng đầy đủ sau khi có Outbox

```
HTTP Request
    │
    ▼
CreateOrderCommandHandler
    ├─ AddAsync(order)         ← EF tracks Order entity
    ├─ PublishAsync(event)     ← EF tracks OutboxMessage entity  [CHƯA vào DB]
    └─ SaveChangesAsync()      ← 1 transaction: commit Order + OutboxMessage ✅

                                        │  (vài trăm ms sau)
                                        ▼
                         BusOutboxDeliveryService (IHostedService)
                             ├─ SELECT OutboxMessage WHERE DeliveredAt IS NULL
                             ├─ Publish to RabbitMQ exchange
                             └─ UPDATE OutboxMessage SET DeliveredAt = NOW()

                                        │
                                        ▼
                              NotificationService Consumer
                              OrderConfirmedConsumer
                              ...
```

---

## Quy tắc viết command handler có Outbox

### Trường hợp 1 — Không cần đọc aggregate sau save (đa số)

Publish **trước** `SaveChanges` → đạt atomic hoàn toàn trong 1 transaction.

```csharp
// ✅ ĐÚNG — CreateOrderCommandHandler
await _orders.AddAsync(order, ct);

await _eventBus.PublishAsync(
    new OrderCreatedIntegrationEvent(...), ct);  // ghi vào EF change tracker

await _uow.SaveChangesAsync(ct);  // 1 transaction: Order + OutboxMessage
```

### Trường hợp 2 — Cần đọc aggregate sau save (tổng hợp, thống kê)

Phải `SaveChanges` lần 1 để đọc số liệu, sau đó `SaveChanges` lần 2 để commit OutboxMessage.

```csharp
// ✅ ĐÚNG — CreateProductCommandHandler
await products.AddAsync(product, ct);
await uow.SaveChangesAsync(ct);              // commit product để đọc tổng

var totalPrice = await products.GetTotalPriceAsync(ct);  // đọc sau save
var totalCount = await products.GetTotalCountAsync(ct);

await eventBus.PublishAsync(
    new ProductCreatedIntegrationEvent(...), ct);  // ghi vào EF change tracker

await uow.SaveChangesAsync(ct);              // commit OutboxMessage
```

> **Lưu ý trường hợp 2**: nếu crash giữa `SaveChanges` #1 và `SaveChanges` #2, business data đã ghi nhưng OutboxMessage chưa. Đây là "best effort" — tốt hơn nhiều so với publish thẳng lên RabbitMQ nhưng không hoàn toàn atomic. Để đạt atomic 100%, cần refactor để không cần đọc aggregate sau save.

### Trường hợp sai — KHÔNG làm

```csharp
// ❌ SAI — publish thẳng lên RabbitMQ (không qua outbox)
await _uow.SaveChangesAsync(ct);
await _eventBus.PublishAsync(event, ct);
// Nếu crash ở đây → event mất vĩnh viễn
```

```csharp
// ❌ SAI — gọi bus.Publish trực tiếp thay vì _eventBus
await _uow.SaveChangesAsync(ct);
await _bus.Publish(event, ct);  // bypass outbox
```

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
using Microsoft.EntityFrameworkCore;

public sealed class NewDbContext : DbContext
{
    public NewDbContext(DbContextOptions<NewDbContext> options) : base(options) { }

    // ... DbSet properties ...

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NewDbContext).Assembly);

        // Outbox + Inbox tables
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        base.OnModelCreating(modelBuilder);
    }
}
```

### Bước 3 — Register trong DependencyInjection.cs

```csharp
// NewService.Infrastructure/DependencyInjection.cs
using MassTransit;

services.AddMassTransitMessaging(configuration, x =>
{
    x.AddEntityFrameworkOutbox<NewDbContext>(o =>
    {
        o.UseSqlServer();   // khớp với provider DB của service
        o.UseBusOutbox();   // bật BusOutboxDeliveryService background worker
    });

    x.AddConsumer<SomeEventConsumer>();
    // thêm consumer khác...
});
```

### Bước 4 — Tạo migration

```bash
~/.dotnet/dotnet ef migrations add AddOutbox \
  --project src/Services/NewService/NewService.Infrastructure \
  --startup-project src/Services/NewService/NewService.API \
  --output-dir Persistence/Migrations
```

Kiểm tra migration sinh ra đúng 3 bảng: `OutboxMessage`, `OutboxState`, `InboxState`.

### Bước 5 — Apply migration

```bash
~/.dotnet/dotnet ef database update \
  --project src/Services/NewService/NewService.Infrastructure \
  --startup-project src/Services/NewService/NewService.API
```

### Bước 6 — Viết command handler theo đúng pattern

Xem mục **Quy tắc viết command handler** ở trên.

---

## Trạng thái hiện tại trong Hdos

| Service | Outbox bật | Services publish | Ghi chú |
|---|---|---|---|
| **OrderService** | ✅ | `OrderCreatedIntegrationEvent` (atomic 100%) | `CreateOrderCommand` đã swap thứ tự |
| **OrderService** | ✅ | `ProductCreatedIntegrationEvent` (best effort) | `CreateProductCommand` cần đọc tổng sau save |
| **M01Service** | ✅ | `BaoCaoKhoaCreatedIntegrationEvent` (best effort) | `CreateBaoCaoKhoaCommand` cần đọc tổng sau save |
| **NotificationService** | ❌ | Không publish integration event | Chỉ consume, không cần outbox publish-side |
| **AuthService** | ❌ | Cần kiểm tra | Chưa rõ có publish không |

---

## Kiểm tra Outbox đang hoạt động

### Xem bảng OutboxMessage trong DB

Khi trigger một command có outbox, query trực tiếp SQL Server:

```sql
-- Xem message vừa được ghi (chưa deliver)
SELECT MessageId, MessageType, Body, EnqueueTime
FROM OutboxMessage
WHERE DeliveredAt IS NULL
ORDER BY EnqueueTime DESC;

-- Xem message đã được deliver
SELECT MessageId, MessageType, DeliveredAt
FROM OutboxMessage
WHERE DeliveredAt IS NOT NULL
ORDER BY DeliveredAt DESC;
```

### Xem log BusOutboxDeliveryService

```bash
docker compose logs order-service --tail=50 | grep -i outbox
```

Nếu hoạt động đúng sẽ thấy log dạng:

```
[Information] MassTransit.BusOutboxDeliveryService: Delivering 1 outbox messages
```

### Kiểm tra via Grafana

Truy cập `http://localhost:3030` → dashboard **MassTransit** → xem metric `masstransit_outbox_delivery_total`.

---

## Troubleshooting

### Message nằm trong OutboxMessage mãi không deliver

**Nguyên nhân thường gặp:**
1. RabbitMQ đang down → background worker retry vô hạn đến khi kết nối lại, không mất message.
2. Service chưa restart sau khi thêm outbox config.
3. `UseBusOutbox()` chưa được gọi trong DI.

**Kiểm tra:**
```bash
# Xem BusOutboxDeliveryService có chạy không
docker compose logs order-service --tail=100 | grep -i "outbox\|delivery"
```

### `InvalidOperationException: The entity type 'OutboxMessage' requires a primary key`

Migration chưa được apply. Chạy `dotnet ef database update`.

### Lỗi `Cannot insert duplicate key` trong `InboxState`

Đây là **hành vi đúng** của inbox deduplication — consumer đang cố xử lý lại message đã xử lý. Outbox đảm bảo at-least-once, consumer cần idempotent để xử lý đúng.

### OutboxMessage bị xóa quá sớm

Mặc định MassTransit giữ OutboxMessage đã deliver trong 24h rồi xóa. Có thể tùy chỉnh:

```csharp
x.AddEntityFrameworkOutbox<OrderDbContext>(o =>
{
    o.UseSqlServer();
    o.UseBusOutbox(b =>
    {
        b.MessageDeliveryLimit = 100;          // số message deliver mỗi lần poll
        b.MessageDeliveryTimeout = TimeSpan.FromSeconds(10);
    });
});
```

---

## Checklist trước khi commit

- [ ] `MassTransit.EntityFrameworkCore` đã thêm vào Infrastructure `.csproj`
- [ ] `AddInboxStateEntity / AddOutboxMessageEntity / AddOutboxStateEntity` đã thêm vào `OnModelCreating`
- [ ] `AddEntityFrameworkOutbox<TDbContext>(o => { o.UseSqlServer(); o.UseBusOutbox(); })` đã register trong DI
- [ ] Migration `AddOutbox` đã tạo và apply
- [ ] Command handler dùng `IEventBus.PublishAsync` (không dùng `IBus.Publish` trực tiếp)
- [ ] Nếu không cần đọc aggregate sau save: `PublishAsync` **trước** `SaveChangesAsync`
- [ ] Nếu cần đọc aggregate: thêm `SaveChangesAsync` **sau** `PublishAsync` để commit OutboxMessage
- [ ] Cập nhật bảng "Trạng thái hiện tại" ở doc này
