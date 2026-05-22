# 17 — MassTransit Messaging

Hệ thống dùng **MassTransit 8.2** làm lớp abstraction trên **RabbitMQ** để truyền integration events giữa các microservices.

---

## Quy tắc đặt tên (đọc trước khi viết)

| Thành phần | Quy tắc đặt tên | Ví dụ |
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
    │   ← message-type exchange: dùng để route theo loại message
    ▼
Exchange: user-logged-in [fanout]
    │   ← endpoint exchange: cùng tên queue, dùng để route tới consumer cụ thể
    ▼
Queue: user-logged-in ──► UserLoggedInConsumer
```

**Tại sao cần 2 exchange?**

- **Message-type exchange** (`Hdos.Contracts.IntegrationEvents:*`): Publisher chỉ cần biết tên event, không cần biết có bao nhiêu consumer. Khi thêm consumer mới ở service khác, publisher không cần sửa gì.
- **Endpoint exchange** (`user-logged-in`): Mỗi consumer có exchange riêng để queue bind vào. Cho phép nhiều consumer cùng nhận một event mà không ảnh hưởng nhau.

**Ví dụ: 2 service cùng subscribe 1 event**

```
Exchange: Hdos.Contracts.IntegrationEvents:UserLoggedInIntegrationEvent [fanout]
    ├── Exchange: user-logged-in [fanout] → Queue: user-logged-in → NotificationService
    └── Exchange: user-logged-in-audit [fanout] → Queue: user-logged-in-audit → AuditService
```

Cả 2 service đều nhận đủ message — RabbitMQ fanout ra tất cả binding.

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

## Cách viết: Thêm event mới từ đầu đến cuối

Ví dụ thực tế: thêm luồng **M01Service publish `BaoCaoKhoaCreatedIntegrationEvent`**, NotificationService nhận và broadcast SSE.

---

### Bước 1 — Tạo Integration Event

File nằm trong project `Contracts` — tất cả services đều có thể reference.

**Vị trí:** `src/BuildingBlocks/Contracts/IntegrationEvents/BaoCaoKhoaCreatedIntegrationEvent.cs`

```csharp
namespace Hdos.Contracts.IntegrationEvents;

public sealed record BaoCaoKhoaCreatedIntegrationEvent(
    int      TongLuotKham,
    decimal  TongDoanhThu,
    decimal  DoanhThuTrungBinhTheoTuan,
    DateTime NgayBaoCao)
    : IntegrationEvent;
```

`IntegrationEvent` (base) tự sinh `EventId` (Guid) và `OccurredOnUtc` (DateTime). Không cần khai báo thêm.

```csharp
// Base class — không cần sửa, chỉ cần inherit
public abstract record IntegrationEvent
{
    public Guid     EventId       { get; init; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
    public string   EventType     => GetType().Name;
}
```

---

### Bước 2 — Publish event từ Command Handler

Inject `IEventBus` vào command handler, gọi `PublishAsync` sau khi lưu DB xong.

**Vị trí:** `src/Services/M01Service/M01Service.Application/Features/BaoCaoKhoa/CreateBaoCaoKhoaCommand.cs`

```csharp
public sealed class CreateBaoCaoKhoaCommandHandler(
    IBaoCaoKhoaRepository repo,
    IUnitOfWork           uow,
    IEventBus             eventBus,
    ILogger<CreateBaoCaoKhoaCommandHandler> logger)
    : IRequestHandler<CreateBaoCaoKhoaCommand, Result<BaoCaoKhoaDto>>
{
    public async Task<Result<BaoCaoKhoaDto>> Handle(
        CreateBaoCaoKhoaCommand command, CancellationToken ct)
    {
        var entity = BaoCaoKhoa.Create(command.TongLuotKham, command.TongDoanhThu);
        await repo.AddAsync(entity, ct);
        await uow.SaveChangesAsync(ct);   // lưu DB trước

        // publish sau khi DB thành công
        await eventBus.PublishAsync(new BaoCaoKhoaCreatedIntegrationEvent(
            TongLuotKham:              entity.TongLuotKham,
            TongDoanhThu:              entity.TongDoanhThu,
            DoanhThuTrungBinhTheoTuan: entity.DoanhThuTrungBinhTheoTuan,
            NgayBaoCao:                entity.NgayBaoCao), ct);

        return Result.Success(entity.ToDto());
    }
}
```

**Chỉ cần 2 bước này là publish hoạt động.** Exchange `bao-cao-khoa-created` sẽ tự được tạo trên RabbitMQ khi message đầu tiên được gửi.

---

### Bước 3 — Viết Application Handler (phía consumer)

Handler chứa business logic, **không import MassTransit**.

**Vị trí:** `src/Services/NotificationService/NotificationService.Application/EventHandlers/BaoCaoKhoaCreatedHandler.cs`

```csharp
using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.Realtime;
using Microsoft.Extensions.Logging;

namespace Hdos.NotificationService.Application.EventHandlers;

public sealed class BaoCaoKhoaCreatedHandler(
    INotificationPusher pusher,
    ILogger<BaoCaoKhoaCreatedHandler> logger)
    : IIntegrationEventHandler<BaoCaoKhoaCreatedIntegrationEvent>
{
    public async Task HandleAsync(BaoCaoKhoaCreatedIntegrationEvent @event, CancellationToken ct)
    {
        logger.LogInformation(
            "Broadcasting bao cao khoa summary for {NgayBaoCao}", @event.NgayBaoCao);

        await pusher.BroadcastEventAsync(
            "bao_cao_khoa_summary",
            new
            {
                tongLuotKham              = @event.TongLuotKham,
                tongDoanhThu              = @event.TongDoanhThu,
                doanhThuTrungBinhTheoTuan = @event.DoanhThuTrungBinhTheoTuan,
                ngayBaoCao                = @event.NgayBaoCao
            },
            ct);
    }
}
```

**Quy tắc viết handler:**
- Inject dependency qua constructor, không qua `IServiceProvider`
- Luôn log `LogInformation` khi bắt đầu xử lý (để trace khi debug)
- Gọi `SaveChangesAsync` **một lần** sau khi xong tất cả DB operations
- **Không bắt exception** — MassTransit retry tự xử lý

---

### Bước 4 — Viết Consumer (Infrastructure)

Consumer là adapter mỏng nối MassTransit với handler, **không chứa logic**.

**Vị trí:** `src/Services/NotificationService/NotificationService.Infrastructure/Consumers/BaoCaoKhoaCreatedConsumer.cs`

```csharp
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.EventHandlers;
using MassTransit;

namespace Hdos.NotificationService.Infrastructure.Consumers;

public sealed class BaoCaoKhoaCreatedConsumer(BaoCaoKhoaCreatedHandler handler)
    : IConsumer<BaoCaoKhoaCreatedIntegrationEvent>
{
    public Task Consume(ConsumeContext<BaoCaoKhoaCreatedIntegrationEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
```

Consumer chỉ có **1 dòng code logic** — delegate xuống handler. Không bao giờ viết thêm gì ở đây.

---

### Bước 5 — Đăng ký Handler và Consumer vào DI

Đăng ký handler **và** consumer trong cùng Infrastructure `DependencyInjection.cs`:

**Vị trí:** `src/Services/NotificationService/NotificationService.Infrastructure/DependencyInjection.cs`

```csharp
public static IServiceCollection AddNotificationInfrastructure(
    this IServiceCollection services, IConfiguration configuration)
{
    // ... DbContext, repositories, v.v.

    // 1. Đăng ký handler (scoped vì inject repository)
    services.AddScoped<BaoCaoKhoaCreatedHandler>();

    // 2. Đăng ký consumer vào MassTransit
    services.AddMassTransitMessaging(configuration, x =>
    {
        x.AddConsumer<BaoCaoKhoaCreatedConsumer>();
        // thêm consumer khác...
    });

    return services;
}
```

Tham khảo thực tế từ **OrderService** (pattern tương tự):

```csharp
// OrderService.Infrastructure/DependencyInjection.cs
services.AddScoped<OrderCreateRequestedEventHandler>();   // handler trước

services.AddMassTransitMessaging(configuration, x =>
{
    x.AddConsumer<OrderCreateRequestedConsumer>();        // consumer sau
});
```

---

### Bước 6 — Kiểm tra kết quả

Sau khi chạy service, vào `http://localhost:15672` — bình thường sẽ thấy **2 exchange** per event (đây là đúng):

Tab **Exchanges**:
```
Hdos.Contracts.IntegrationEvents:BaoCaoKhoaCreatedIntegrationEvent  [fanout]  ← message-type
bao-cao-khoa-created                                                 [fanout]  ← endpoint
```

Tab **Queues**:
```
bao-cao-khoa-created   ← bind vào endpoint exchange cùng tên
```

Click vào exchange `Hdos.Contracts.IntegrationEvents:BaoCaoKhoaCreatedIntegrationEvent` → phần **Bindings** sẽ thấy nó bind tới `bao-cao-khoa-created` (endpoint exchange).

---

## Tổng hợp các events hiện tại

Mỗi row = 1 consumer. Mỗi consumer tạo ra **2 exchange** trong RabbitMQ (message-type + endpoint).

| Integration Event | Message-type exchange | Endpoint exchange / Queue | Publisher | Consumer | Handler |
|---|---|---|---|---|---|
| `UserRegisteredIntegrationEvent` | `Hdos.Contracts…:UserRegisteredIntegrationEvent` | `user-registered` | AuthService | `UserRegisteredConsumer` | `UserRegisteredEventHandler` |
| `UserLoggedInIntegrationEvent` | `Hdos.Contracts…:UserLoggedInIntegrationEvent` | `user-logged-in` | AuthService | `UserLoggedInConsumer` | `UserLoggedInEventHandler` |
| `OrderCreateRequestedIntegrationEvent` | `Hdos.Contracts…:OrderCreateRequestedIntegrationEvent` | `order-create-requested` | ApiGateway | `OrderCreateRequestedConsumer` | `OrderCreateRequestedEventHandler` |
| `OrderCreatedIntegrationEvent` | `Hdos.Contracts…:OrderCreatedIntegrationEvent` | `order-created` | OrderService | `OrderCreatedConsumer` | `OrderCreatedEventHandler` |
| `OrderConfirmedIntegrationEvent` | `Hdos.Contracts…:OrderConfirmedIntegrationEvent` | `order-confirmed` | OrderService | `OrderConfirmedConsumer` | `OrderConfirmedEventHandler` |
| `NotificationSendRequestedIntegrationEvent` | `Hdos.Contracts…:NotificationSendRequestedIntegrationEvent` | `notification-send-requested` | ApiGateway | `NotificationSendRequestedConsumer` | `NotificationSendRequestedEventHandler` |
| `ProductCreatedIntegrationEvent` | `Hdos.Contracts…:ProductCreatedIntegrationEvent` | `product-created` | OrderService | `ProductCreatedConsumer` | `ProductCreatedEventHandler` |
| `ProductCreatedIntegrationEvent` | `Hdos.Contracts…:ProductCreatedIntegrationEvent` | `product-total-updated` | OrderService | `ProductTotalUpdatedConsumer` | `ProductTotalUpdatedHandler` |
| `BaoCaoKhoaCreatedIntegrationEvent` | `Hdos.Contracts…:BaoCaoKhoaCreatedIntegrationEvent` | `bao-cao-khoa-created` | M01Service | `BaoCaoKhoaCreatedConsumer` | `BaoCaoKhoaCreatedHandler` |
| `TestIntegrationEvent` | `Hdos.Contracts…:TestIntegrationEvent` | `test` | ApiGateway | `TestConsumer` | `TestIntegrationEventHandler` |
| `HoanggggfIntegrationEvent` | `Hdos.Contracts…:HoanggggfIntegrationEvent` | `hoanggggf` | ApiGateway | `HoanggggfConsumer` | `HoanggggfEventHandler` |
| `HoanggggfIntegrationEvent` | `Hdos.Contracts…:HoanggggfIntegrationEvent` | `hoanggggf-error` | ApiGateway | `HoanggggfErrorConsumer` | *(demo retry/dead-letter)* |

---

## Trường hợp ngoại lệ: Consumer tên khác event

Khi consumer không theo đúng quy ước đặt tên (ví dụ `ProductTotalUpdatedConsumer` nhưng consume `ProductCreatedIntegrationEvent`), RabbitMQ sẽ tạo **2 exchange riêng biệt** thay vì merge:

```
Exchange: product-created [fanout]          ← message-type exchange
    └── Exchange: product-total-updated [fanout]   ← endpoint exchange
            └── Queue: product-total-updated
```

Vẫn hoạt động đúng, chỉ là có thêm 1 exchange trong RabbitMQ. Nên tránh trường hợp này trừ khi có lý do rõ ràng (như `ProductTotalUpdatedConsumer` — xử lý tổng kết từ event tạo sản phẩm).

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
    └─ Hết retry → message chuyển sang: bao-cao-khoa-created_error
```

Xem log retry trong `HoanggggfErrorConsumer` — consumer cố tình throw lỗi để test cơ chế này.

### Xem và re-process message lỗi

1. Vào `http://localhost:15672` → **Queues** → `bao-cao-khoa-created_error`
2. **Get messages** để xem nội dung và exception
3. **Move messages** → nhập queue gốc (`bao-cao-khoa-created`) để retry lại

---

## Dọn dẹp exchange cũ (chạy 1 lần sau khi đổi naming)

Exchange cũ dạng `Hdos.Contracts.IntegrationEvents:*` là **durable** — không tự xóa khi restart service. Cần xóa thủ công:

1. Restart tất cả services với code mới trước:
   ```bash
   docker compose down && docker compose up --build -d
   ```
2. Vào `http://localhost:15672` → **Exchanges**
3. Xóa từng exchange có prefix `Hdos.Contracts.IntegrationEvents:`

Sau khi xóa, chúng sẽ không được tạo lại vì tất cả services đã dùng formatter mới.

---

## Cấu hình RabbitMQ (ServiceCollectionExtensions)

```csharp
services.AddMassTransit(x =>
{
    // Queue name = kebab-case của consumer class (bỏ suffix "Consumer")
    x.SetKebabCaseEndpointNameFormatter();

    configure?.Invoke(x);   // đăng ký consumer ở đây

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(hostUri, h =>
        {
            h.Username(options.UserName);
            h.Password(options.Password);
        });

        // Retry exponential: 5 lần, từ 1s đến 30s
        cfg.UseMessageRetry(r => r.Exponential(
            retryLimit:    5,
            minInterval:   TimeSpan.FromSeconds(1),
            maxInterval:   TimeSpan.FromSeconds(30),
            intervalDelta: TimeSpan.FromSeconds(5)));

        cfg.ConfigureEndpoints(ctx);
    });
});
```

Message-type exchange dùng **full namespace mặc định của MassTransit** (`Hdos.Contracts.IntegrationEvents:XxxIntegrationEvent`). Không custom formatter — custom formatter gây ra self-binding khi tên exchange trùng tên endpoint exchange, dẫn đến message bị deliver 2 lần.

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

- [ ] Event nằm trong `Contracts` project, record kế thừa `IntegrationEvent`
- [ ] Tên consumer = `{EventName bỏ IntegrationEvent}Consumer` (để exchange merge về 1)
- [ ] Handler implement `IIntegrationEventHandler<TEvent>`, không import MassTransit
- [ ] Handler được `AddScoped` trong Infrastructure `DependencyInjection.cs`
- [ ] Consumer implement `IConsumer<TEvent>`, chỉ delegate sang handler, không có logic
- [ ] Consumer được `AddConsumer<T>()` trong `AddMassTransitMessaging()`
- [ ] Cập nhật bảng "Tổng hợp các events hiện tại" ở trên
