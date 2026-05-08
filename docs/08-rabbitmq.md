# 08 — Messaging RabbitMQ

Mô hình: **topic exchange + per-consumer queue**. Tất cả implementation đặt
trong `BuildingBlocks/Common/Messaging/`, các service chỉ ráp publisher /
consumer.

## 1. Topology

```
                                    ┌─────────────────────────┐
[AuthService]      publish ────────►│                         │
   UserRegistered                   │                         │
   UserLoggedIn                     │   exchange (topic)      │
                                    │   "hdos.events"         │ durable
[OrderService]     publish ────────►│                         │
   OrderCreated                     │                         │
                                    └────┬────────────┬───────┘
                                         │ rk = tên class event
                              ┌──────────┘            │
                              ▼                       ▼
                ┌────────────────────────┐   ┌───────────────────────────┐
                │ queue                  │   │ queue                     │
                │ notification.user-     │   │ notification.order-       │
                │   logged-in            │   │   created                 │
                │  bind: UserLoggedIn... │   │  bind: OrderCreated...    │
                └──────┬─────────────────┘   └──────────┬────────────────┘
                       │                                │
                       ▼                                ▼
            UserLoggedInConsumer            OrderCreatedConsumer
            (BackgroundService trong NotificationService)
```

Tất cả 3 cái: **exchange durable, queue durable, message persistent**, manual ack.

Routing key = `typeof(TEvent).Name` (vd `"OrderCreatedIntegrationEvent"`). Tên
class chính là contract — đừng đổi tên class một cách tuỳ tiện.

## 2. Cấu hình

`RabbitMqOptions` (file `Common/Messaging/RabbitMqOptions.cs`) đọc từ section
`RabbitMq` trong `appsettings.json`:

```json
{
  "RabbitMq": {
    "Host": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "VirtualHost": "/",
    "Exchange": "hdos.events",
    "RetryCount": 5
  }
}
```

Trong Docker compose override bằng env var:

```yaml
environment:
  RabbitMq__Host: rabbitmq
  RabbitMq__Port: 5672
```

## 3. `RabbitMqConnection` (singleton)

File: `Common/Messaging/RabbitMqConnection.cs`

- Mở `IConnection` lazy, retry tới `RetryCount` lần với backoff (RabbitMQ
  thường chậm khởi động cùng container Auth/Order).
- Cung cấp `CreateChannel()` cho publisher / consumer.
- Singleton — connection nặng, channel nhẹ. Nguyên tắc AMQP: **share
  connection, đừng share channel** giữa các thread.

## 4. Publisher — `IEventBus`

```csharp
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IntegrationEvent;
}
```

Implementation `RabbitMqEventBus` (`Common/Messaging/RabbitMqEventBus.cs`):

```csharp
public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
    where TEvent : IntegrationEvent
{
    using var channel = _connection.CreateChannel();
    channel.ExchangeDeclare(_options.Exchange, ExchangeType.Topic, durable: true);

    var routingKey = typeof(TEvent).Name;
    var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event, @event.GetType()));

    var props = channel.CreateBasicProperties();
    props.Persistent = true;
    props.MessageId = @event.EventId.ToString();
    props.Type = routingKey;
    props.ContentType = "application/json";
    props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    channel.BasicPublish(
        exchange: _options.Exchange,
        routingKey: routingKey,
        mandatory: false,
        basicProperties: props,
        body: body);

    return Task.CompletedTask;
}
```

Đăng ký một dòng trong `Infrastructure/DependencyInjection.cs`:

```csharp
services.AddRabbitMq(configuration);   // singleton connection + IEventBus
```

Sử dụng từ handler:

```csharp
public CreateOrderCommandHandler(..., IEventBus eventBus) { _eventBus = eventBus; }

await _eventBus.PublishAsync(
    new OrderCreatedIntegrationEvent(order.Id, order.CustomerId, ...), ct);
```

> ⚠️ Hiện tại publish **không** transactional với DB. Nếu commit DB xong rồi
> service crash trước khi publish, message mất. Production nên triển khai
> *Outbox pattern* — ghi event vào bảng outbox cùng transaction, có job riêng
> đọc và publish. Tham khảo "Next steps" trong README gốc.

## 5. Consumer — `RabbitMqConsumerHostedService<TEvent, THandler>`

File: `Common/Messaging/RabbitMqConsumerHostedService.cs`

Generic abstract class, kế thừa `BackgroundService`. Mỗi service consume
event chỉ cần khai báo subclass + tên queue:

```csharp
public sealed class OrderCreatedConsumer
    : RabbitMqConsumerHostedService<OrderCreatedIntegrationEvent, OrderCreatedEventHandler>
{
    public OrderCreatedConsumer(
        RabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<OrderCreatedConsumer> logger)
        : base(connection, options, scopeFactory, logger,
            queueName: "notification.order-created") { }
}
```

Đăng ký:

```csharp
services.AddHostedService<OrderCreatedConsumer>();
```

### Bên trong base class

```csharp
protected override Task ExecuteAsync(CancellationToken stoppingToken)
{
    _channel = _connection.CreateChannel();
    var routingKey = typeof(TEvent).Name;

    _channel.ExchangeDeclare(_options.Exchange, ExchangeType.Topic, durable: true);
    _channel.QueueDeclare(_queueName, durable: true, exclusive: false, autoDelete: false);
    _channel.QueueBind(_queueName, _options.Exchange, routingKey);
    _channel.BasicQos(0, 10, false);     // prefetch = 10

    var consumer = new AsyncEventingBasicConsumer(_channel);
    consumer.Received += OnMessageAsync;
    _channel.BasicConsume(_queueName, autoAck: false, consumer);
    return Task.CompletedTask;
}

private async Task OnMessageAsync(object sender, BasicDeliverEventArgs ea)
{
    try
    {
        var @event = JsonSerializer.Deserialize<TEvent>(ea.Body.ToArray());
        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<THandler>();
        await handler.HandleAsync(@event!, CancellationToken.None);

        _channel!.BasicAck(ea.DeliveryTag, multiple: false);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to handle event {RoutingKey}", ea.RoutingKey);
        _channel!.BasicNack(ea.DeliveryTag, multiple: false, requeue: !ea.Redelivered);
    }
}
```

Bốn điểm quan trọng:

1. **Auto-declare** — exchange/queue/binding declare khi service khởi động.
   Không cần script tạo trước.
2. **Manual ack** — chỉ ack khi handler xong. Service crash giữa chừng →
   message vẫn nằm trong queue.
3. **Scoped DbContext** — `_scopeFactory.CreateScope()` cho mỗi message.
   Tuyệt đối không inject `DbContext` thẳng vào consumer (singleton).
4. **Retry-on-fail** — `requeue: !ea.Redelivered` ⇒ thử lại 1 lần. Lần 2 fail
   → drop. Mục đích: tránh **poison pill loop** (1 message luôn fail làm queue
   nghẽn). Production nên đẩy vào DLX (dead-letter exchange) thay vì drop —
   xem mục mở rộng cuối doc.

## 6. Handler — `IIntegrationEventHandler<TEvent>`

```csharp
public interface IIntegrationEventHandler<in TEvent> where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}
```

Implementation đặt trong `*.Application/EventHandlers/`. Handler là `Scoped`,
phải đăng ký rõ ràng (không tự discovery):

```csharp
services.AddScoped<OrderCreatedEventHandler>();
```

Trong handler có thể dùng `IUnitOfWork`, repository, gọi service ngoài… như
một MediatR handler bình thường. Tự handle idempotency nếu nghiệp vụ cần
(message có thể giao 2 lần — xem mục "Mở rộng").

## 7. End-to-end flow `OrderCreated`

```
[OrderService]                     [RabbitMQ]                 [NotificationService]
─────────────────                  ──────────                  ──────────────────────

CreateOrderCommandHandler
   _eventBus.PublishAsync
   ├─ ExchangeDeclare hdos.events (idempotent)
   ├─ JsonSerializer.Serialize(@event)
   └─ BasicPublish(exchange="hdos.events",
                   routingKey="OrderCreatedIntegrationEvent",
                   props.Persistent=true)
                                   exchange routes by rk
                                   ├─► queue "notification.order-created"
                                   └─► (chưa có consumer khác bind cùng rk)

                                                              OrderCreatedConsumer
                                                                 .OnMessageAsync
                                                                  ├─ deserialize
                                                                  ├─ CreateScope
                                                                  ├─ handler.HandleAsync
                                                                  │     ├─ Notification.Create
                                                                  │     ├─ MarkSent
                                                                  │     └─ SaveChangesAsync
                                                                  └─ BasicAck
```

Quan sát qua RabbitMQ UI [http://localhost:15672](http://localhost:15672):

- **Exchanges** → `hdos.events` → tab Bindings.
- **Queues** → 3 queue `notification.*` → xem `Messages` (nếu Notification
  down sẽ thấy số message nằm chờ).
- **Connections** / **Channels** → debug khi consumer disconnect bất thường.

## 8. Mở rộng / nâng cấp khi cần

| Vấn đề                             | Giải pháp                                                                        |
|------------------------------------|----------------------------------------------------------------------------------|
| Mất message khi DB commit ↔ publish race | **Outbox pattern**: ghi `outbox_events` cùng tx, job riêng publish.       |
| Cần delivery exactly-once nghiệp vụ | **Idempotency key**: handler check `event.EventId` trong bảng `processed_events`. |
| Poison pill                         | Replace `requeue` với DLX: `x-dead-letter-exchange` khi declare queue.            |
| Cần scale handler                   | Tăng số worker / replica của consumer service. Queue cùng tên ⇒ competing consumer. |
| Cần đảm bảo thứ tự                  | RabbitMQ giữ thứ tự *trong 1 queue, 1 consumer*. Đa consumer ⇒ mất thứ tự.        |
| Cần broker khác (Kafka, ASB)       | Implement `IEventBus` mới + replace `RabbitMqConsumerHostedService`. Service không phải sửa. |
