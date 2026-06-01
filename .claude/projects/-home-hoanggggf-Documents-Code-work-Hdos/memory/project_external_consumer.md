---
name: external-consumer-pattern
description: ExternalConsumer attribute pattern cho RabbitMQ consumers nhận messages từ hệ thống bên ngoài — cách khai báo và cơ chế auto-registration
metadata:
  type: project
---

Pattern `[ExternalConsumer("queue-name")]` để nhận messages từ external systems qua RabbitMQ, implemented tại BuildingBlocks.

**Why:** Tránh phải sửa DI file mỗi khi thêm external consumer mới. Mỗi consumer hoàn toàn độc lập (queue riêng, prefetch riêng, handler riêng).

**How to apply:** Khi user muốn thêm consumer nhận message từ hệ thống bên ngoài, chỉ cần tạo class + attribute — không cần hướng dẫn sửa DI.

Key files:
- `BuildingBlocks/Common/Messaging/ExternalConsumerAttribute.cs` — attribute, kế thừa `ExcludeFromConfigureEndpointsAttribute`
- `BuildingBlocks/Common/Messaging/ExternalConsumerExtensions.cs` — `AddExternalConsumers` + `ConfigureExternalEndpoints`
- `Common/Extensions/ServiceCollectionExtensions.cs` — `AddMassTransitMessaging` có param `externalConsumersAssembly`

Cách dùng:
```csharp
// Chỉ tạo class này, không cần sửa gì thêm:
[ExternalConsumer("queue-name", PrefetchCount = 20)]
public sealed class MyConsumer(MyHandler handler) : IConsumer<MyMessage>
{
    public Task Consume(ConsumeContext<MyMessage> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
```

DI setup một lần tại service:
```csharp
services.AddMassTransitMessaging(configuration, x => { ... },
    servicePrefix: "notification",
    externalConsumersAssembly: typeof(DependencyInjection).Assembly);
```

Docs: `docs/27-external-consumer-pattern.md`
