# 28 — Message Payload Standard

Quy chuẩn cấu trúc message cho tất cả events chạy qua RabbitMQ trong hệ thống Hdos, áp dụng cho cả **internal events** (giữa các services nội bộ) và **external messages** (nhận từ hệ thống bên ngoài).

Chuẩn tham chiếu: **CloudEvents v1.0** — CNCF specification (https://cloudevents.io), được dùng bởi Azure Event Grid, AWS EventBridge, GCP Eventarc, Kafka, Dapr.

---

## Tại sao cần chuẩn hóa?

Không có quy chuẩn → mỗi event tự định nghĩa fields theo ý → không trace được request xuyên services → không biết event từ đâu → không quản lý được schema version khi payload thay đổi.

| Vấn đề | Không có chuẩn | Có chuẩn |
|---|---|---|
| Trace request qua nhiều services | Không thể | `CorrelationId` xuyên suốt |
| Biết event từ service nào | Đoán qua queue name | `Source` rõ ràng |
| Breaking change payload | Crash consumer | Tăng `Version`, handle cả 2 |
| Debug message lỗi trong _error queue | Xem payload mù | Đọc `EventType`, `Source`, `CorrelationId` |

---

## Internal Events — `IntegrationEvent`

Dùng cho events giữa các services nội bộ trong Hdos (publish qua `IEventBus`, nhận qua `IConsumer<T>`).

### Cấu trúc

```csharp
// BuildingBlocks/Contracts/IntegrationEvents/IntegrationEvent.cs
public abstract record IntegrationEvent
{
    public Guid     EventId       { get; init; } = Guid.NewGuid();
    public string   EventType     => GetType().Name;
    public string   Source        { get; init; } = string.Empty;
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
    public string   CorrelationId { get; init; } = string.Empty;
    public string?  CausationId   { get; init; }
    public string   Version       { get; init; } = "1.0";
}
```

### Mapping với CloudEvents

| Field Hdos | CloudEvents | Kiểu | Mô tả |
|---|---|---|---|
| `EventId` | `id` | `Guid` | Unique ID của event. Dùng để deduplicate ở consumer (idempotency) |
| `EventType` | `type` | `string` | Tên class event, auto-computed. VD: `"UserRegisteredIntegrationEvent"` |
| `Source` | `source` | `string` | Service đã publish. VD: `"AuthService"`, `"OrderService"` |
| `OccurredOnUtc` | `time` | `DateTime` | Thời điểm sự kiện xảy ra, luôn là UTC |
| `CorrelationId` | `correlationid` | `string` | Trace ID xuyên suốt 1 request qua nhiều services |
| `CausationId` | `causationid` | `string?` | EventId của event/command gây ra event này. Dùng để build event chain |
| `Version` | `schemaversion` | `string` | Phiên bản schema payload. Tăng khi có breaking change |

### Ví dụ JSON trên RabbitMQ (MassTransit envelope)

```json
{
  "messageType": ["urn:message:Hdos.Contracts.IntegrationEvents:UserRegisteredIntegrationEvent"],
  "message": {
    "eventId":       "a1b2c3d4-0000-0000-0000-000000000000",
    "eventType":     "UserRegisteredIntegrationEvent",
    "source":        "AuthService",
    "occurredOnUtc": "2026-06-01T08:30:00.000Z",
    "correlationId": "4bf92f3577b34da6a3ce929d0e0e4736",
    "causationId":   null,
    "version":       "1.0",
    "userId":        "...",
    "email":         "user@hdos.dev",
    "fullName":      "Nguyen Van A"
  }
}
```

### Auto-enrichment — không cần set thủ công

`MassTransitEventBus` tự động enrich trước khi publish:

```
Source        ← IHostEnvironment.ApplicationName  (tên service từ config)
CorrelationId ← Activity.Current.TraceId          (OpenTelemetry W3C trace)
```

Vì vậy khi viết event handler chỉ cần tạo event với business data, **không cần tự set Source hay CorrelationId**:

```csharp
await eventBus.PublishAsync(new UserRegisteredIntegrationEvent(
    UserId:   user.Id,
    Email:    user.Email,
    FullName: user.FullName), ct);
// Source = "AuthService", CorrelationId = trace ID → tự động
```

### Khi nào dùng `CausationId`

Dùng khi muốn build event chain để trace luồng:

```
HTTP POST /orders
  └─► CreateOrderCommand
        └─► OrderCreatedDomainEvent  (causationId = null, đây là gốc)
              └─► OrderCreatedIntegrationEvent  (causationId = OrderCreatedDomainEvent.Id)
                    └─► NotificationSentIntegrationEvent  (causationId = OrderCreatedIntegrationEvent.EventId)
```

```csharp
// Trong OrderCreatedIntegrationEventHandler:
await eventBus.PublishAsync(new OrderCreatedIntegrationEvent(...) with
{
    CausationId = notification.DomainEventId.ToString()
}, ct);
```

### Khi nào tăng `Version`

| Loại thay đổi | Version | Cần làm gì |
|---|---|---|
| Thêm field nullable mới | Giữ `"1.0"` | Consumer cũ bỏ qua field, không cần sửa |
| Đổi tên field | Tăng `"2.0"` | Consumer cần handle cả `"1.0"` và `"2.0"` trong thời gian chuyển đổi |
| Xóa field bắt buộc | Tăng `"2.0"` | Cần deploy consumer mới trước, publisher sau |

---

## External Messages — `ExternalMessage`

Dùng cho messages nhận từ hệ thống bên ngoài (HIS, ERP, legacy systems…) qua RabbitMQ.  
Xem cách đăng ký consumer: [27 — External Consumer Pattern](./27-external-consumer-pattern.md).

### Cấu trúc base

```csharp
// BuildingBlocks/Contracts/ExternalMessages/ExternalMessage.cs
public abstract record ExternalMessage
{
    public string?   MessageId     { get; init; }
    public string?   Source        { get; init; }
    public string?   EventType     { get; init; }
    public DateTime? OccurredAt    { get; init; }
    public string?   CorrelationId { get; init; }
    public string?   Version       { get; init; }
}
```

Tất cả fields **nullable** vì không kiểm soát được format bên ngoài gửi.

### Mapping với CloudEvents

| Field Hdos | CloudEvents | Mô tả |
|---|---|---|
| `MessageId` | `id` | ID message do bên ngoài cấp |
| `Source` | `source` | Tên hệ thống gửi. VD: `"his-01"`, `"erp-main"` |
| `EventType` | `type` | Loại sự kiện. VD: `"patient.discharged"`, `"invoice.created"` |
| `OccurredAt` | `time` | Thời điểm sự kiện ở hệ thống ngoài |
| `CorrelationId` | `correlationid` | Trace ID nếu bên ngoài hỗ trợ |
| `Version` | `schemaversion` | Phiên bản schema do bên ngoài định nghĩa |

### Cách tạo message mới từ external system

Kế thừa `ExternalMessage`, thêm fields riêng của payload:

```csharp
// Application/DTOs/LabResultMessage.cs
public sealed record LabResultMessage(
    string?      PatientId,
    string?      TestCode,
    JsonElement? Result,
    DateTime?    TestedAt) : ExternalMessage;
```

Consumer đăng ký với `[ExternalConsumer]`:

```csharp
// Infrastructure/Consumers/LabResultConsumer.cs
[ExternalConsumer("external.lab-result")]
public sealed class LabResultConsumer(LabResultHandler handler)
    : IConsumer<LabResultMessage>
{
    public Task Consume(ConsumeContext<LabResultMessage> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
```

### Ví dụ JSON từ hệ thống ngoài (raw, không có envelope)

```json
{
  "messageId":     "ext-20260601-00123",
  "source":        "his-01",
  "eventType":     "patient.discharged",
  "occurredAt":    "2026-06-01T07:00:00.000Z",
  "correlationId": "abc-xyz-123",
  "version":       "1.0",
  "patientId":     "BN-456",
  "testCode":      "CBC",
  "result":        { "wbc": 7.2, "rbc": 4.8 },
  "testedAt":      "2026-06-01T06:45:00.000Z"
}
```

> Nếu bên ngoài không gửi `messageId`, `source`... — các field đó sẽ là `null`. Handler cần kiểm tra trước khi dùng.

---

## So sánh Internal vs External

| | Internal (`IntegrationEvent`) | External (`ExternalMessage`) |
|---|---|---|
| **Publisher** | Services nội bộ Hdos | Hệ thống bên ngoài |
| **Format** | MassTransit envelope (có `messageType`) | Raw JSON (không có envelope) |
| **Fields** | Non-nullable, có default | Nullable, không kiểm soát |
| **Source** | Auto-set từ `ApplicationName` | Do bên ngoài gửi (hoặc null) |
| **CorrelationId** | Auto-set từ OpenTelemetry trace | Do bên ngoài gửi (hoặc null) |
| **Deserializer** | MassTransit default | `UseRawJsonDeserializer` |
| **Consumer đăng ký** | `x.AddConsumer<T>()` + `cfg.ConfigureEndpoints` | `[ExternalConsumer("queue")]` attribute |
| **Base class** | `IntegrationEvent` | `ExternalMessage` |

---

## Checklist khi tạo event mới

### Internal event

- [ ] Record kế thừa `IntegrationEvent` — không khai báo lại `EventId`, `Source`, `CorrelationId`...
- [ ] Tên class: `{Tên}IntegrationEvent`
- [ ] Nếu thay đổi breaking field: tăng `Version` và handle cả 2 version ở consumer
- [ ] Nếu muốn trace event chain: set `CausationId = parentEvent.EventId.ToString()`

### External message

- [ ] Record kế thừa `ExternalMessage`
- [ ] Dùng `JsonElement?` cho fields có thể là object/array từ bên ngoài
- [ ] Consumer đặt `[ExternalConsumer("tên-queue")]`
- [ ] Handler kiểm tra null trước khi dùng các field từ `ExternalMessage`
