# 17 — MassTransit Messaging

Hệ thống dùng **MassTransit 8.2** làm lớp abstraction trên **RabbitMQ** để truyền integration events giữa các microservices.

**Docs liên quan:**
- [21 — Transactional Outbox Pattern](./21-outbox-pattern.md) — đảm bảo event không bị mất khi publish
- [27 — External Consumer Pattern](./27-external-consumer-pattern.md) — nhận messages từ hệ thống bên ngoài không dùng MassTransit envelope

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

**External consumers** (nhận từ hệ thống bên ngoài, xem [doc 27](./27-external-consumer-pattern.md)):

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

Để test cơ chế này, viết một consumer tạm thời throw exception và quan sát queue `{name}_error` trên RabbitMQ Management.

### Re-process message lỗi

1. Vào `http://localhost:15672` → **Queues** → `{queue}_error`
2. **Get messages** để xem nội dung và exception
3. **Move messages** → nhập queue gốc để retry lại

---

## Test End-to-End

Dùng luồng async order để kiểm chứng publish → consumer.

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

Nếu `consumer_count = 0`:
```bash
docker compose restart notificationservice
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

## Checklist trước khi commit

- [ ] Event nằm trong `Contracts` project, record kế thừa `IntegrationEvent`
- [ ] Tên consumer = `{EventName bỏ IntegrationEvent}Consumer` (để exchange merge về 1)
- [ ] Handler implement `IIntegrationEventHandler<TEvent>`, không import MassTransit
- [ ] Consumer implement `IConsumer<TEvent>`, chỉ delegate sang handler, không có logic
- [ ] Consumer được `AddConsumer<T>()` trong `AddMassTransitMessaging()`
- [ ] Handler được `AddScoped` trong Infrastructure `DependencyInjection.cs`
- [ ] Cập nhật bảng "Tổng hợp các events hiện tại" ở trên
- [ ] Nếu cần Outbox: xem [21 — Outbox Pattern](./21-outbox-pattern.md)
- [ ] Nếu nhận từ hệ thống ngoài: xem [27 — External Consumer Pattern](./27-external-consumer-pattern.md)
