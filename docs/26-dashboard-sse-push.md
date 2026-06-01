# 26 — Dashboard SSE Push: RabbitMQ → NotificationService → Frontend

Khi `MatchingWorker` xử lý xong một batch records, nó publish `DashboardFeReadyIntegrationEvent` qua MassTransit. `NotificationService` nhận event rồi broadcast SSE xuống tất cả frontend đang kết nối.

**Docs liên quan:**
- [21 — Outbox Pattern](./21-outbox-pattern.md) — đảm bảo event không mất khi DataMatchingService publish
- [14 — SSE Realtime](./14-sse-realtime.md) — cơ chế SSE và SseConnectionManager
- [27 — External Consumer Pattern](./27-external-consumer-pattern.md) — nếu nhận thêm events từ hệ thống bên ngoài

---

## Flow tổng thể

```
DataMatchingService
  └── MatchingWorker (chạy mỗi 30s)
        │  xử lý xong batch → IEventBus.PublishAsync(DashboardFeReadyIntegrationEvent)
        │  EF Core Outbox lưu message vào DB cùng transaction SaveChangesAsync
        ▼
RabbitMQ
  Exchange: dashboard-fe-ready [fanout]        ← tự tạo bởi MassTransit publisher
        │
        ▼
  Queue: notification-dashboard-fe-ready       ← tự tạo bởi MassTransit consumer
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

## Publisher — DataMatchingService

`MatchingWorker` là **BackgroundService** chạy định kỳ, không có HTTP request trigger:

```csharp
// DataMatchingService.Infrastructure/Workers/MatchingWorker.cs
private async Task ProcessBatchAsync(CancellationToken ct)
{
    using var scope = scopeFactory.CreateScope();
    var records  = scope.ServiceProvider.GetRequiredService<IStagingRecordRepository>();
    var uow      = scope.ServiceProvider.GetRequiredService<IDataMatchingUnitOfWork>();
    var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

    var batch = await records.GetPendingBatchAsync(50, ct);
    if (batch.Count == 0) return;

    var affectedSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var processed = 0;

    foreach (var record in batch)
    {
        // ... match logic ...
        affectedSystems.Add(record.SourceSystem);
        processed++;
    }

    if (processed > 0)
        await eventBus.PublishAsync(
            new DashboardFeReadyIntegrationEvent(processed, [.. affectedSystems]), ct);

    await uow.SaveChangesAsync(ct);  // commit records đã match + outbox message cùng 1 transaction
}
```

> `IEventBus` phải lấy từ cùng `IServiceScope` với `IDataMatchingUnitOfWork` để outbox hoạt động trong cùng DbContext transaction.

**Contract:**

```csharp
// BuildingBlocks/Contracts/IntegrationEvents/DashboardFeReadyIntegrationEvent.cs
public sealed record DashboardFeReadyIntegrationEvent(
    int      ProcessedCount,
    string[] AffectedSystems)
    : IntegrationEvent;
```

---

## Consumer — NotificationService

`DashboardFeReadyConsumer` là **consumer chuẩn** — không có gì đặc biệt, MassTransit tự tạo queue `notification-dashboard-fe-ready`:

```csharp
// NotificationService.Infrastructure/Consumers/DashboardFeReadyConsumer.cs
public sealed class DashboardFeReadyConsumer(DashboardFeReadyHandler handler)
    : IConsumer<DashboardFeReadyIntegrationEvent>
{
    public Task Consume(ConsumeContext<DashboardFeReadyIntegrationEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
```

Đăng ký DI bình thường:

```csharp
// NotificationService.Infrastructure/DependencyInjection.cs
services.AddMassTransitMessaging(configuration, x =>
{
    // ...
    x.AddConsumer<DashboardFeReadyConsumer>();
    // ...
}, servicePrefix: "notification");
```

---

## Handler — NotificationService

```csharp
// NotificationService.Application/EventHandlers/DashboardFeReadyHandler.cs
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
            }, ct);
    }
}
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
const es = new EventSource(`/notifications/sse?access_token=${token}`);

es.addEventListener('notification', (e) => {
  const msg = JSON.parse(e.data);

  if (msg.type === 'dashboard-fe-ready') {
    const { affectedSystems, processedCount } = msg.payload;

    if (affectedSystems.includes(currentSourceSystem)) {
      fetchDashboard(currentDashboardCode, currentSourceSystem, currentDate);
    }
  }
});
```

---

## Topology trong RabbitMQ Management

Sau khi khởi động, `http://localhost:15672` sẽ hiện:

```
Exchanges:
  Hdos.Contracts.IntegrationEvents:DashboardFeReadyIntegrationEvent [fanout]
      └── binding → Exchange: dashboard-fe-ready
  dashboard-fe-ready [fanout]
      └── binding → Queue: notification-dashboard-fe-ready

Queues:
  notification-dashboard-fe-ready   (consumer: DashboardFeReadyConsumer)
```

---

## Test thủ công

### 1. Kiểm tra queue có consumer

RabbitMQ Management → Queues → `notification-dashboard-fe-ready` → tab **Consumers** → phải thấy ít nhất 1 consumer active.

### 2. Mở SSE stream

```bash
curl -N "http://localhost:5000/notifications/sse?access_token=<jwt>"
```

Phải nhận ngay `: connected`.

### 3. Publish message thủ công vào exchange

RabbitMQ Management → Exchanges → `dashboard-fe-ready` → **Publish message**:

```json
{
  "messageType": ["urn:message:Hdos.Contracts.IntegrationEvents:DashboardFeReadyIntegrationEvent"],
  "message": {
    "processedCount": 5,
    "affectedSystems": ["his-01"],
    "eventId": "00000000-0000-0000-0000-000000000001",
    "occurredOnUtc": "2026-05-29T10:00:00Z"
  }
}
```

### 4. Kết quả mong đợi

Terminal `curl` SSE phải nhận:

```
event: notification
data: {"type":"dashboard-fe-ready","payload":{"processedCount":5,"affectedSystems":["his-01"]},"occurredAtUtc":"..."}
```

Log NotificationService:

```
Broadcasting dashboard-fe-ready: 5 records, systems=[his-01]
SSE broadcast event | type=dashboard-fe-ready → N connections
```
