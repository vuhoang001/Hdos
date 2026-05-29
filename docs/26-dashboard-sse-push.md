# 26 — Dashboard SSE Push: RabbitMQ → NotificationService → Frontend

Khi `MatchingWorker` xử lý xong một batch records, nó publish event `DashboardFeReadyIntegrationEvent`.
`NotificationService` nhận event từ queue `be.hdos.dashboard.fe.ready` rồi broadcast SSE xuống tất cả frontend đang kết nối.

---

## Flow tổng thể

```
DataMatchingService
  └── MatchingWorker (chạy mỗi 30s)
        │  xử lý xong batch → IEventBus.PublishAsync(DashboardFeReadyIntegrationEvent)
        │  EF Core Outbox lưu message vào DB cùng transaction SaveChangesAsync
        ▼
RabbitMQ
  Exchange: dashboard-fe-ready [fanout]   ← tự tạo bởi MassTransit publisher
        │
        ▼
  Queue: be.hdos.dashboard.fe.ready       ← tên custom, bind vào exchange trên
        │
        ▼
NotificationService
  └── DashboardFeReadyConsumer
        └── DashboardFeReadyHandler
              └── INotificationPusher.BroadcastEventAsync("dashboard-fe-ready", ...)
                    │
                    ▼
              SseConnectionManager → tất cả Channel<string> đang mở
                    │
                    ▼
              Browser (EventSource) nhận event → refresh dashboard
```

---

## Tại sao consumer này khác với những consumer còn lại?

Đây là câu hỏi quan trọng. Hầu hết consumer trong hệ thống (VD: `BaoCaoKhoaCreatedConsumer`, `UserLoggedInConsumer`) đi theo **luồng chuẩn** — MassTransit tự đặt tên queue. Consumer này phá vỡ quy tắc đó để bind vào một **queue có tên cố định từ trước**.

### So sánh: Consumer chuẩn vs Consumer này

| Tiêu chí | Consumer chuẩn (vd: BaoCaoKhoa) | DashboardFeReadyConsumer |
|---|---|---|
| **Đặt tên queue** | Auto: `notification-bao-cao-khoa-created` | Custom: `be.hdos.dashboard.fe.ready` |
| **Cấu hình endpoint** | `ConfigureEndpoints(ctx)` tự xử lý | `ReceiveEndpoint(name, ...)` khai báo tường minh |
| **Attribute** | Không cần | `[ExcludeFromConfigureEndpoints]` bắt buộc |
| **Nơi đăng ký** | Chỉ `AddConsumer<T>()` là đủ | Cần thêm `configureReceiveEndpoints` callback |
| **Publisher** | Command Handler (request-time) | `MatchingWorker` (background, 30s interval) |
| **Outbox** | Phụ thuộc service (M01 không có) | DataMatchingService có EF Core Outbox |

---

## Giải thích chi tiết từng điểm khác biệt

### 1. Queue name cố định (`be.hdos.dashboard.fe.ready`)

Với consumer chuẩn, MassTransit dùng `KebabCaseEndpointNameFormatter(prefix, false)` để đặt tên queue tự động:

```
DashboardFeReadyConsumer  →  notification-dashboard-fe-ready
```

Nhưng queue `be.hdos.dashboard.fe.ready` đã tồn tại (được tạo thủ công hoặc bởi hệ thống ngoài). Để bind consumer vào đúng queue này, phải dùng `ReceiveEndpoint` với tên tường minh:

```csharp
cfg.ReceiveEndpoint("be.hdos.dashboard.fe.ready", e =>
{
    e.ConfigureConsumer<DashboardFeReadyConsumer>(ctx);
});
```

### 2. Attribute `[ExcludeFromConfigureEndpoints]`

Khi gọi `cfg.ConfigureEndpoints(ctx)`, MassTransit lặp qua **tất cả** consumer đã đăng ký và tạo endpoint cho từng cái. Nếu không đánh dấu consumer này, MassTransit sẽ tạo THÊM một queue thứ hai `notification-dashboard-fe-ready` — consumer nhận message từ hai queue, xử lý trùng:

```
❌ Không có [ExcludeFromConfigureEndpoints]:
   Queue: be.hdos.dashboard.fe.ready       → DashboardFeReadyConsumer (mong muốn)
   Queue: notification-dashboard-fe-ready  → DashboardFeReadyConsumer (TRÙNG)
   → Mỗi message được xử lý 2 lần

✅ Có [ExcludeFromConfigureEndpoints]:
   Queue: be.hdos.dashboard.fe.ready       → DashboardFeReadyConsumer (đúng)
   → Mỗi message được xử lý đúng 1 lần
```

### 3. Extension `configureReceiveEndpoints` trong `AddMassTransitMessaging`

Mã gốc chỉ cho phép cấu hình `IBusRegistrationConfigurator` (đăng ký consumer). Để bind custom endpoint, cần truy cập `IRabbitMqBusFactoryConfigurator` — bên trong `UsingRabbitMq((ctx, cfg) => ...)`. Vì vậy đã thêm tham số mới:

```csharp
// Common/Extensions/ServiceCollectionExtensions.cs
public static IServiceCollection AddMassTransitMessaging(
    this IServiceCollection services,
    IConfiguration configuration,
    Action<IBusRegistrationConfigurator>? configure = null,
    string servicePrefix = "",
    Action<IRabbitMqBusFactoryConfigurator, IBusRegistrationContext>? configureReceiveEndpoints = null)
{
    // ...
    x.UsingRabbitMq((ctx, cfg) =>
    {
        // ...
        configureReceiveEndpoints?.Invoke(cfg, ctx);  // custom endpoint TRƯỚC
        cfg.ConfigureEndpoints(ctx);                   // auto-config những consumer còn lại
    });
}
```

Thứ tự quan trọng: `configureReceiveEndpoints` phải gọi **trước** `ConfigureEndpoints` để MassTransit biết consumer nào đã được cấu hình thủ công.

### 4. Publisher là Background Worker, không phải Command Handler

Tất cả event khác được publish trong **Command Handler** — tức là ngay khi có HTTP request:

```
HTTP POST /bao-cao-khoa  →  CreateBaoCaoKhoaCommandHandler  →  PublishAsync(BaoCaoKhoaCreatedEvent)
```

`DashboardFeReadyIntegrationEvent` được publish trong `MatchingWorker` — một **BackgroundService** chạy định kỳ:

```
MatchingWorker (mỗi 30s)  →  xử lý batch records  →  PublishAsync(DashboardFeReadyEvent)
```

Không có HTTP request nào trigger. Đây là **server-initiated push**: backend tự quyết định khi nào dữ liệu đã sẵn sàng và thông báo frontend.

### 5. EF Core Outbox đảm bảo không mất message

DataMatchingService dùng **Transactional Outbox Pattern**. `IEventBus.PublishAsync` không gửi thẳng lên RabbitMQ mà ghi vào bảng outbox trong cùng transaction với `SaveChangesAsync`:

```csharp
// MatchingWorker.cs
if (processed > 0)
    await eventBus.PublishAsync(
        new DashboardFeReadyIntegrationEvent(processed, [.. affectedSystems]), ct);

await uow.SaveChangesAsync(ct);  // lưu records đã match + outbox message cùng 1 transaction
```

MassTransit `BusOutbox` worker sau đó đọc outbox và forward lên RabbitMQ. Đảm bảo:
- Nếu DB commit thành công → message chắc chắn được gửi (dù RabbitMQ tạm down)
- Nếu DB rollback → message cũng không được gửi (không bao giờ gửi dữ liệu lỗi)

Xem thêm: [21 — Transactional Outbox Pattern](./21-outbox-pattern.md)

---

## Các file liên quan

### Contract (shared)

**`src/BuildingBlocks/Contracts/IntegrationEvents/DashboardFeReadyIntegrationEvent.cs`**

```csharp
public sealed record DashboardFeReadyIntegrationEvent(
    int      ProcessedCount,
    string[] AffectedSystems)
    : IntegrationEvent;
```

- `ProcessedCount`: số record đã match thành công trong batch
- `AffectedSystems`: danh sách source system có dữ liệu mới (VD: `["his-01", "lis-02"]`)

---

### Publisher — DataMatchingService

**`src/Services/DataMatchingService/DataMatchingService.Infrastructure/Workers/MatchingWorker.cs`**

```csharp
private async Task ProcessBatchAsync(CancellationToken ct)
{
    using var scope = scopeFactory.CreateScope();
    var records  = scope.ServiceProvider.GetRequiredService<IStagingRecordRepository>();
    var uow      = scope.ServiceProvider.GetRequiredService<IDataMatchingUnitOfWork>();
    var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

    var batch = await records.GetPendingBatchAsync(50, ct);
    if (batch.Count == 0) return;   // không có gì → không publish

    var affectedSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var processed = 0;

    foreach (var record in batch)
    {
        // ... match logic ...
        affectedSystems.Add(record.SourceSystem);
        processed++;
    }

    if (processed > 0)
        await eventBus.PublishAsync(                       // (1) ghi vào outbox
            new DashboardFeReadyIntegrationEvent(processed, [.. affectedSystems]), ct);

    await uow.SaveChangesAsync(ct);                        // (2) commit DB + outbox atomically
}
```

> **Lưu ý:** `IEventBus` phải lấy từ cùng `IServiceScope` với `IDataMatchingUnitOfWork` để outbox hoạt động trong cùng DbContext transaction.

---

### Consumer — NotificationService.Infrastructure

**`src/Services/NotificationService/NotificationService.Infrastructure/Consumers/DashboardFeReadyConsumer.cs`**

```csharp
[ExcludeFromConfigureEndpoints]   // không để ConfigureEndpoints tạo queue trùng
public sealed class DashboardFeReadyConsumer(DashboardFeReadyHandler handler)
    : IConsumer<DashboardFeReadyIntegrationEvent>
{
    public Task Consume(ConsumeContext<DashboardFeReadyIntegrationEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
```

---

### Handler — NotificationService.Application

**`src/Services/NotificationService/NotificationService.Application/EventHandlers/DashboardFeReadyHandler.cs`**

```csharp
public sealed class DashboardFeReadyHandler(
    INotificationPusher pusher,
    ILogger<DashboardFeReadyHandler> logger)
    : IIntegrationEventHandler<DashboardFeReadyIntegrationEvent>
{
    public async Task HandleAsync(DashboardFeReadyIntegrationEvent @event, CancellationToken ct)
    {
        logger.LogInformation(
            "Broadcasting dashboard-fe-ready: {Count} records, systems=[{Systems}]",
            @event.ProcessedCount, string.Join(", ", @event.AffectedSystems));

        await pusher.BroadcastEventAsync(
            "dashboard-fe-ready",
            new
            {
                processedCount  = @event.ProcessedCount,
                affectedSystems = @event.AffectedSystems
            },
            ct);
    }
}
```

---

### Đăng ký DI — NotificationService.Infrastructure

**`src/Services/NotificationService/NotificationService.Infrastructure/DependencyInjection.cs`**

```csharp
services.AddMassTransitMessaging(configuration, x =>
{
    // ... consumer chuẩn ...
    x.AddConsumer<DashboardFeReadyConsumer>();   // đăng ký để MassTransit biết type
    // ...
}, servicePrefix: "notification", configureReceiveEndpoints: (cfg, ctx) =>
{
    cfg.ReceiveEndpoint("be.hdos.dashboard.fe.ready", e =>
    {
        e.ConfigureConsumer<DashboardFeReadyConsumer>(ctx);  // bind vào queue tên cố định
    });
});
```

---

## SSE event format nhận được ở frontend

```
event: notification
data: {
  "type": "dashboard-fe-ready",
  "payload": {
    "processedCount": 42,
    "affectedSystems": ["his-01", "lis-02"]
  },
  "occurredAtUtc": "2026-05-29T10:00:00Z"
}

```

### JavaScript integration

```javascript
const token = localStorage.getItem('access_token');
const es = new EventSource(`/notifications/sse?access_token=${token}`);

es.addEventListener('notification', (e) => {
  const msg = JSON.parse(e.data);

  if (msg.type === 'dashboard-fe-ready') {
    const { affectedSystems, processedCount } = msg.payload;
    console.log(`${processedCount} records processed for: ${affectedSystems.join(', ')}`);

    // Chỉ refresh nếu dashboard đang hiển thị thuộc hệ thống bị ảnh hưởng
    if (affectedSystems.includes(currentSourceSystem)) {
      fetchDashboard(currentDashboardCode, currentSourceSystem, currentDate);
    }
  }
});

es.onerror = () => {
  // EventSource tự reconnect sau 3s — không cần xử lý thêm
};
```

---

## Topology trong RabbitMQ Management

Sau khi service khởi động và message đầu tiên được publish, RabbitMQ Management UI (`http://localhost:15672`) sẽ hiện:

```
Exchanges:
  dashboard-fe-ready [fanout]
      └── binding → Queue: be.hdos.dashboard.fe.ready

Queues:
  be.hdos.dashboard.fe.ready   (consumer: DashboardFeReadyConsumer)
```

Exchange `dashboard-fe-ready` được tạo tự động bởi MassTransit khi DataMatchingService publish lần đầu. Queue `be.hdos.dashboard.fe.ready` được tạo khi NotificationService khởi động và đăng ký `ReceiveEndpoint`.

---

## Test thủ công

### 1. Kiểm tra queue có consumer

Vào RabbitMQ Management → Queues → `be.hdos.dashboard.fe.ready` → tab **Consumers**. Phải thấy ít nhất 1 consumer đang active.

### 2. Mở SSE stream

```bash
curl -N "http://localhost:5000/notifications/sse?access_token=<jwt>"
```

Phải nhận ngay `: connected` (comment SSE).

### 3. Publish message thủ công vào queue

Vào RabbitMQ Management → Queues → `be.hdos.dashboard.fe.ready` → **Publish message**:

```json
{
  "processedCount": 5,
  "affectedSystems": ["his-01"],
  "eventId": "00000000-0000-0000-0000-000000000001",
  "occurredOnUtc": "2026-05-29T10:00:00Z",
  "eventType": "DashboardFeReadyIntegrationEvent"
}
```

### 4. Kết quả mong đợi

Terminal đang `curl` SSE phải nhận:

```
event: notification
data: {"type":"dashboard-fe-ready","payload":{"processedCount":5,"affectedSystems":["his-01"]},"occurredAtUtc":"..."}
```

Log NotificationService phải có:

```
Broadcasting dashboard-fe-ready: 5 records processed, systems=[his-01]
SSE broadcast event | type=dashboard-fe-ready → N connections
```

---

## Checklist thêm consumer dùng queue tên cố định

Khi cần bind một consumer vào queue có tên không theo convention MassTransit:

- [ ] Tạo `IntegrationEvent` trong `Contracts/IntegrationEvents/`
- [ ] Thêm `[ExcludeFromConfigureEndpoints]` vào Consumer class
- [ ] Đăng ký Consumer bình thường với `x.AddConsumer<T>()`
- [ ] Thêm `configureReceiveEndpoints` callback với `cfg.ReceiveEndpoint(tênQueue, ...)`
- [ ] Thêm `using MassTransit;` vào file DI nếu chưa có (cần cho `e.ConfigureConsumer<T>()`)
- [ ] Kiểm tra Infrastructure `.csproj` có reference `MassTransit.RabbitMQ`

Xem thêm: [17 — MassTransit Messaging](./17-masstransit-messaging.md) cho luồng consumer chuẩn.
