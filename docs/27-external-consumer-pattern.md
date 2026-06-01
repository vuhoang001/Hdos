# 27 — External Consumer Pattern

Pattern để nhận messages từ **các ứng dụng bên ngoài** (không dùng MassTransit) thông qua RabbitMQ, với mỗi consumer hoàn toàn độc lập và cách khai báo đơn giản bằng attribute.

---

## Vấn đề cần giải quyết

Các service nội bộ giao tiếp qua MassTransit — envelope, typing, retry đều được MassTransit tự xử lý. Nhưng khi nhận messages từ **hệ thống bên ngoài** (third-party HIS, ERP, hệ thống legacy…), chúng ta phải đối mặt với:

- Message format tùy ý, không có MassTransit envelope
- Tên queue/exchange do bên ngoài quy định, không theo convention nội bộ
- Mỗi source system có thể cần prefetch count và concurrency khác nhau
- Số lượng source tăng dần theo thời gian → không muốn sửa file DI mỗi lần thêm mới

---

## Giải pháp: `[ExternalConsumer]` Attribute

Đặt attribute lên consumer class → system tự scan, tự tạo queue, tự wire up. Mỗi consumer hoàn toàn độc lập.

```
[ExternalConsumer("tên-queue")]   ← khai báo một lần
     ↓
ExternalConsumerExtensions        ← scan assembly lúc startup
     ↓
RabbitMQ ReceiveEndpoint riêng    ← queue độc lập, prefetch riêng
```

---

## Cấu trúc

### Attribute — `ExternalConsumerAttribute`

```
BuildingBlocks/Common/Messaging/ExternalConsumerAttribute.cs
```

```csharp
[ExternalConsumer("tên-queue")]
[ExternalConsumer("tên-queue", UseRawJson = false)]
[ExternalConsumer("tên-queue", PrefetchCount = 50, ConcurrentLimit = 10)]
```

| Property | Mặc định | Ý nghĩa |
|---|---|---|
| `QueueName` | *(bắt buộc)* | Tên queue trên RabbitMQ |
| `UseRawJson` | `true` | Bật raw JSON deserializer cho messages không dùng MassTransit envelope |
| `PrefetchCount` | `10` | Số messages prefetch từ broker |
| `ConcurrentLimit` | `5` | Số messages xử lý đồng thời |

Attribute này kế thừa `ExcludeFromConfigureEndpointsAttribute` của MassTransit — tức là `cfg.ConfigureEndpoints()` sẽ **bỏ qua** consumer này, tránh tạo endpoint trùng.

### Extension Methods — `ExternalConsumerExtensions`

```
BuildingBlocks/Common/Messaging/ExternalConsumerExtensions.cs
```

Hai extension method được gọi tự động bên trong `AddMassTransitMessaging` khi truyền `externalConsumersAssembly`:

- `AddExternalConsumers(assembly)` — đăng ký consumer types vào MassTransit DI
- `ConfigureExternalEndpoints(ctx, assembly)` — tạo `ReceiveEndpoint` riêng cho mỗi consumer

---

## Cách thêm consumer mới

### Bước 1 — Tạo message contract (nếu chưa có)

```csharp
// Application/DTOs/LabResultMessage.cs
namespace Hdos.NotificationService.Application.DTOs;

public sealed record LabResultMessage(
    string? PatientId,
    string? TestCode,
    string? Result,
    DateTime? TestedAt);
```

### Bước 2 — Tạo handler trong Application layer

```csharp
// Application/EventHandlers/LabResultHandler.cs
namespace Hdos.NotificationService.Application.EventHandlers;

public sealed class LabResultHandler(
    INotificationPusher pusher,
    ILogger<LabResultHandler> logger)
{
    public async Task HandleAsync(LabResultMessage message, CancellationToken ct)
    {
        logger.LogInformation("Lab result received for patient {PatientId}", message.PatientId);

        await pusher.BroadcastEventAsync(
            "lab-result",
            new { patientId = message.PatientId, testCode = message.TestCode, result = message.Result },
            ct);
    }
}
```

### Bước 3 — Tạo consumer với `[ExternalConsumer]`

```csharp
// Infrastructure/Consumers/LabResultConsumer.cs
using Hdos.Common.Messaging;
using Hdos.NotificationService.Application.DTOs;
using Hdos.NotificationService.Application.EventHandlers;
using MassTransit;

namespace Hdos.NotificationService.Infrastructure.Consumers;

[ExternalConsumer("external.lab-result", PrefetchCount = 20, ConcurrentLimit = 8)]
public sealed class LabResultConsumer(LabResultHandler handler)
    : IConsumer<LabResultMessage>
{
    public Task Consume(ConsumeContext<LabResultMessage> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
```

**Xong.** Không cần sửa bất kỳ file nào khác.

---

## Setup một lần trong DependencyInjection.cs

```csharp
services.AddMassTransitMessaging(configuration, x =>
{
    // Internal consumers — đăng ký thủ công như bình thường
    x.AddConsumer<UserLoggedInConsumer>();
    x.AddConsumer<OrderCreatedConsumer>();
    // ...

    // External consumers KHÔNG đăng ký ở đây — [ExternalConsumer] tự xử lý
},
servicePrefix: "notification",
externalConsumersAssembly: typeof(DependencyInjection).Assembly);  // ← scan assembly này
```

---

## Cơ chế hoạt động (lúc startup)

```
AddMassTransitMessaging()
    │
    ├── configure?.Invoke(x)           ← internal consumers đăng ký thủ công
    │
    ├── x.AddExternalConsumers(asm)    ← scan [ExternalConsumer], đăng ký vào DI
    │        └── x.AddConsumer(type) per found type
    │
    └── UsingRabbitMq(...)
             │
             ├── cfg.ConfigureExternalEndpoints(ctx, asm)
             │        └── per found type:
             │             ├── cfg.ReceiveEndpoint(attr.QueueName, ...)
             │             ├── e.PrefetchCount = attr.PrefetchCount
             │             ├── e.ConcurrentMessageLimit = attr.ConcurrentLimit
             │             ├── e.UseRawJsonDeserializer()  [nếu UseRawJson = true]
             │             └── e.ConfigureConsumer(ctx, type)
             │
             └── cfg.ConfigureEndpoints(ctx)   ← bỏ qua [ExternalConsumer] classes
```

---

## Consumers hiện có

| Consumer | Queue | Source | PrefetchCount |
|---|---|---|---|
| `ProcessedToFeConsumer` | `be.hdos.dashboard.fe.ready` | DataMatchingService | 10 (mặc định) |

---

## So sánh với cách cũ

**Trước** — phải sửa 2 chỗ mỗi khi thêm external consumer:

```csharp
// DependencyInjection.cs — phải thêm dòng này:
x.AddConsumer<NewConsumer>();

// Và phải thêm endpoint này:
configureReceiveEndpoints: (cfg, ctx) => {
    cfg.ReceiveEndpoint("external.new-queue", e => {
        e.UseRawJsonDeserializer();
        e.ConfigureConsumer<NewConsumer>(ctx);
    });
}
```

**Sau** — chỉ cần thêm attribute vào consumer class, không động đến DI:

```csharp
[ExternalConsumer("external.new-queue")]
public sealed class NewConsumer(...) : IConsumer<NewMessage> { ... }
```

---

## Troubleshooting

### Consumer không nhận được message

1. Kiểm tra tên queue trong attribute có khớp với queue trên RabbitMQ không (RabbitMQ Management UI: `http://localhost:15672`)
2. Kiểm tra binding giữa exchange và queue đã được thiết lập chưa
3. Log startup có dòng `Receiving messages from ...` với tên queue tương ứng không

### Deserialization lỗi

- Mặc định `UseRawJson = true` — nếu message vẫn lỗi, kiểm tra các field trong record có khớp với JSON thực tế không
- Dùng `object?` hoặc `JsonElement?` cho các field có thể thay đổi format

### Muốn tắt raw JSON deserializer

Bên ngoài gửi đúng MassTransit envelope:

```csharp
[ExternalConsumer("internal.special-queue", UseRawJson = false)]
public sealed class SpecialConsumer(...) : IConsumer<SpecialMessage> { ... }
```

### Tìm tất cả external consumers trong codebase

```bash
grep -r "\[ExternalConsumer" src/ --include="*.cs"
```
