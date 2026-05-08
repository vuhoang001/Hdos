# 06 — Feature: NotificationService

`NotificationService` là service **chỉ consume**: nó không có form đăng ký
notification, mọi noti đều sinh ra do *nghe* event từ service khác qua RabbitMQ.

## 1. Endpoints

| Method | Path                  | Use case                       | MediatR request                    |
|--------|-----------------------|--------------------------------|------------------------------------|
| GET    | `/notifications`      | List 50 noti gần nhất (mặc định)| `ListRecentNotificationsQuery(take)` |
| GET    | `/notifications/health`| Health check                   | (controller trực tiếp)             |

Ngoài ra service có **3 BackgroundService** consume event (xem mục 4).

## 2. Domain (`NotificationService.Domain`)

### `Notification : AggregateRoot<Guid>`

```csharp
public enum NotificationChannel { Email = 0, Sms = 1, Push = 2 }
public enum NotificationStatus  { Pending = 0, Sent = 1, Failed = 2 }

public static Notification Create(string recipient, string subject, string body,
    NotificationChannel channel = NotificationChannel.Email);

public void MarkSent();
public void MarkFailed(string reason);
```

Lifecyle: `Pending` → (handler gửi xong) → `Sent` hoặc `Failed`.

Hiện tại handler đơn giản chỉ `MarkSent()` ngay sau khi `Create()` (chưa thật
sự gửi mail) — đây là chỗ bạn sẽ cắm SendGrid / SMTP / FCM vào.

## 3. Use case `ListRecentNotifications`

File: `Application/Features/ListNotifications/ListRecentNotificationsQuery.cs`

Read-only: chỉ truy vấn DB và map sang DTO.

## 4. Tiêu thụ event qua RabbitMQ

Đây là **giá trị chính** của service. Có 3 consumer, mỗi cái nghe 1 routing key
riêng và đẩy vào 1 handler riêng:

| Consumer (BackgroundService)     | Queue                            | Routing key                     | Handler                          |
|----------------------------------|----------------------------------|---------------------------------|----------------------------------|
| `UserRegisteredConsumer`         | `notification.user-registered`   | `UserRegisteredIntegrationEvent`| `UserRegisteredEventHandler`     |
| `UserLoggedInConsumer`           | `notification.user-logged-in`    | `UserLoggedInIntegrationEvent`  | `UserLoggedInEventHandler`       |
| `OrderCreatedConsumer`           | `notification.order-created`     | `OrderCreatedIntegrationEvent`  | `OrderCreatedEventHandler`       |

### 4.1 Consumer (Infrastructure)

File: `Infrastructure/Consumers/UserLoggedInConsumer.cs`

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

Toàn bộ logic plumbing nằm ở
`BuildingBlocks/Common/Messaging/RabbitMqConsumerHostedService.cs`. Consumer
class chỉ cần khai báo `<TEvent, THandler>` + tên queue.

### 4.2 Handler (Application)

File: `Application/EventHandlers/UserLoggedInEventHandler.cs`

```csharp
public sealed class OrderCreatedEventHandler : IIntegrationEventHandler<OrderCreatedIntegrationEvent>
{
    private readonly INotificationRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<OrderCreatedEventHandler> _logger;

    public async Task HandleAsync(OrderCreatedIntegrationEvent @event, CancellationToken ct)
    {
        _logger.LogInformation("Received OrderCreated {OrderId} for {Email}",
            @event.OrderId, @event.CustomerEmail);

        var lines = string.Join("\n",
            @event.Items.Select(i => $" - {i.ProductName} x{i.Quantity} @ {i.UnitPrice}"));

        var notification = Notification.Create(
            recipient: @event.CustomerEmail,
            subject: $"Order {@event.OrderId:N} confirmed",
            body: $"Thanks for your order!\nTotal: {@event.TotalAmount}\nItems:\n{lines}");

        notification.MarkSent();
        await _repo.AddAsync(notification, ct);
        await _uow.SaveChangesAsync(ct);
    }
}
```

Handler là `Scoped` — `RabbitMqConsumerHostedService` tự `CreateScope()` cho
từng message để DbContext không bị share giữa nhiều message song song.

### 4.3 Đăng ký DI

File: `Infrastructure/DependencyInjection.cs`

```csharp
services.AddDbContext<NotificationDbContext>(...);
services.AddScoped<INotificationRepository, NotificationRepository>();
services.AddScoped<IUnitOfWork, UnitOfWork>();

services.AddRabbitMq(configuration);                    // singleton connection + IEventBus

services.AddHostedService<UserLoggedInConsumer>();      // 3 BackgroundService chạy parallel
services.AddHostedService<UserRegisteredConsumer>();
services.AddHostedService<OrderCreatedConsumer>();
```

Application thì cần đăng ký handler để consumer resolve được:

File: `Application/DependencyInjection.cs`:

```csharp
services.AddScoped<UserLoggedInEventHandler>();
services.AddScoped<UserRegisteredEventHandler>();
services.AddScoped<OrderCreatedEventHandler>();
```

(Vì handler không phải MediatR request, không tự discovery — đăng ký rõ ràng.)

## 5. End-to-end demo flow Order → Notification

```
[Client] POST /orders                 ──► [OrderService]
                                              │
                                              │ (gRPC) verify user → AuthService
                                              │
                                              │ Order.Create + persist
                                              │
                                              ▼
                                  [RabbitMQ]
                                  exchange: hdos.events
                                  routing : OrderCreatedIntegrationEvent
                                              │
                                              ▼
                          queue notification.order-created
                                              │
                                              ▼
                              [NotificationService]
                              OrderCreatedConsumer.OnMessageAsync
                                  ├── deserialize JSON → event
                                  ├── CreateScope()
                                  ├── handler.HandleAsync
                                  │     ├── Notification.Create(recipient=email,...)
                                  │     ├── MarkSent()
                                  │     ├── _repo.AddAsync
                                  │     └── _uow.SaveChangesAsync
                                  └── BasicAck (hoặc BasicNack/requeue nếu fail)
```

Mở RabbitMQ UI [http://localhost:15672](http://localhost:15672) (guest/guest)
sau khi `docker compose up` để xem 3 queue, message rate, và message còn tồn
nếu consumer down.
