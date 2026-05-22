# 17 — MassTransit Messaging

Hệ thống dùng **MassTransit 8.2** làm lớp abstraction trên **RabbitMQ** để truyền integration events giữa các microservices.

---

## Quy tắc đặt tên (đọc trước khi viết)

Đây là quy tắc **bắt buộc** để exchange và queue không bị nhân đôi trong RabbitMQ.

| Thành phần | Quy tắc đặt tên | Ví dụ |
|---|---|---|
| **Integration Event** | `{Tên}IntegrationEvent` | `UserLoggedInIntegrationEvent` |
| **Exchange RabbitMQ** | Tên event, bỏ `IntegrationEvent`, kebab-case | `user-logged-in` |
| **Consumer** | `{Tên}Consumer` — bỏ `IntegrationEvent` so với event | `UserLoggedInConsumer` |
| **Queue RabbitMQ** | Tên consumer, bỏ `Consumer`, kebab-case | `user-logged-in` |
| **Application Handler** | `{Tên}EventHandler` hoặc `{Tên}Handler` | `UserLoggedInEventHandler` |

Khi đặt tên đúng quy ước: **exchange = queue = cùng 1 tên** → RabbitMQ chỉ tạo 1 exchange duy nhất.

```
UserLoggedInIntegrationEvent
    ↓ bỏ "IntegrationEvent", kebab-case
user-logged-in  ← tên exchange (message-type)

UserLoggedInConsumer
    ↓ bỏ "Consumer", kebab-case
user-logged-in  ← tên queue (endpoint)

→ exchange = queue = "user-logged-in" ✅ merge thành 1
```

---

## Topology trong RabbitMQ

```
Publisher
    │
    ▼
Exchange: user-logged-in [fanout]  ← 1 exchange duy nhất
    │
    ├── Queue: user-logged-in ──► UserLoggedInConsumer (NotificationService)
    │
    └── Queue: user-logged-in-audit ──► UserLoggedInAuditConsumer (AuditService, nếu có)
```

Nếu nhiều service cùng subscribe 1 event → mỗi service có **queue riêng** → cả hai đều nhận đủ message.

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

Sau khi chạy service, vào `http://localhost:15672` → tab **Exchanges**:

```
bao-cao-khoa-created   [fanout]   ← exchange duy nhất, không có dạng Hdos.Contracts.*
```

Tab **Queues**:

```
bao-cao-khoa-created   ← queue bind vào exchange cùng tên
```

---

## Tổng hợp các events hiện tại

| Integration Event | Exchange / Queue | Publisher | Consumer | Handler |
|---|---|---|---|---|
| `UserRegisteredIntegrationEvent` | `user-registered` | AuthService | `UserRegisteredConsumer` | `UserRegisteredEventHandler` |
| `UserLoggedInIntegrationEvent` | `user-logged-in` | AuthService | `UserLoggedInConsumer` | `UserLoggedInEventHandler` |
| `OrderCreateRequestedIntegrationEvent` | `order-create-requested` | ApiGateway | `OrderCreateRequestedConsumer` | `OrderCreateRequestedEventHandler` |
| `OrderCreatedIntegrationEvent` | `order-created` | OrderService | `OrderCreatedConsumer` | `OrderCreatedEventHandler` |
| `OrderConfirmedIntegrationEvent` | `order-confirmed` | OrderService | `OrderConfirmedConsumer` | `OrderConfirmedEventHandler` |
| `NotificationSendRequestedIntegrationEvent` | `notification-send-requested` | ApiGateway | `NotificationSendRequestedConsumer` | `NotificationSendRequestedEventHandler` |
| `ProductCreatedIntegrationEvent` | `product-created` | OrderService | `ProductCreatedConsumer` | `ProductCreatedEventHandler` |
| `ProductCreatedIntegrationEvent` | `product-created` → `product-total-updated` | OrderService | `ProductTotalUpdatedConsumer` | `ProductTotalUpdatedHandler` |
| `BaoCaoKhoaCreatedIntegrationEvent` | `bao-cao-khoa-created` | M01Service | `BaoCaoKhoaCreatedConsumer` | `BaoCaoKhoaCreatedHandler` |
| `TestIntegrationEvent` | `test` | ApiGateway | `TestConsumer` | `TestIntegrationEventHandler` |
| `HoanggggfIntegrationEvent` | `hoanggggf` | ApiGateway | `HoanggggfConsumer` | `HoanggggfEventHandler` |
| `HoanggggfIntegrationEvent` | `hoanggggf` → `hoanggggf-error` | ApiGateway | `HoanggggfErrorConsumer` | *(intentional error, demo retry)* |

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
x.UsingRabbitMq((ctx, cfg) =>
{
    // Đặt tên exchange = kebab-case, bỏ suffix "IntegrationEvent"
    cfg.MessageTopology.SetEntityNameFormatter(new KebabCaseEntityNameFormatter());

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
```

`KebabCaseEntityNameFormatter` (trong `Common/Messaging/NameFormatterExtensions.cs`):

```csharp
internal class KebabCaseEntityNameFormatter : IEntityNameFormatter
{
    private const string Suffix = "IntegrationEvent";

    public string FormatEntityName<T>()
    {
        var name = typeof(T).Name;
        // Guard: base class "IntegrationEvent" không bị strip thành empty
        if (name.EndsWith(Suffix, StringComparison.Ordinal) && name.Length > Suffix.Length)
            name = name[..^Suffix.Length];
        return KebabCaseEndpointNameFormatter.Instance.SanitizeName(name);
    }
}
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

- [ ] Event nằm trong `Contracts` project, record kế thừa `IntegrationEvent`
- [ ] Tên consumer = `{EventName bỏ IntegrationEvent}Consumer` (để exchange merge về 1)
- [ ] Handler implement `IIntegrationEventHandler<TEvent>`, không import MassTransit
- [ ] Handler được `AddScoped` trong Infrastructure `DependencyInjection.cs`
- [ ] Consumer implement `IConsumer<TEvent>`, chỉ delegate sang handler, không có logic
- [ ] Consumer được `AddConsumer<T>()` trong `AddMassTransitMessaging()`
- [ ] Cập nhật bảng "Tổng hợp các events hiện tại" ở trên
