# 07 — Giao tiếp nội bộ

Hệ thống có hai loại giao tiếp giữa services:

| Loại | Công nghệ | Khi dùng |
|------|-----------|---------|
| Synchronous | gRPC | Cần kết quả ngay để tiếp tục xử lý |
| Asynchronous | RabbitMQ | Thông báo sự kiện đã xảy ra, không cần chờ |

---

## gRPC (Synchronous)

### Tại sao gRPC thay vì HTTP REST?

| | gRPC | REST (JSON) |
|--|------|------------|
| Protocol | Binary (Protobuf) | Text (JSON) |
| Performance | ~5-10x nhanh hơn | Baseline |
| Contract | `.proto` file (strict) | OpenAPI (optional) |
| Code generation | Tự động (server + client) | Manual |
| Streaming | Hỗ trợ native | Phức tạp |
| Browser support | Cần proxy | Native |

**Kết luận:** gRPC tốt cho internal service-to-service. REST tốt cho external (browser/mobile).

### Contract: `users.proto`
```protobuf
// src/BuildingBlocks/Contracts/Grpc/users.proto
syntax = "proto3";
package users;
option csharp_namespace = "Hdos.Contracts.Grpc";

service Users {
    rpc GetUserById (GetUserByIdRequest) returns (UserResponse);
    rpc UserExists  (UserExistsRequest)  returns (UserExistsResponse);
}

message GetUserByIdRequest  { string user_id = 1; }
message UserExistsRequest   { string user_id = 1; }

message UserResponse {
    string user_id    = 1;
    string email      = 2;
    string full_name  = 3;
}

message UserExistsResponse { bool exists = 1; }
```

### Server (AuthService)
```csharp
// AuthService.API/Grpc/UserGrpcService.cs
public class UserGrpcService : Users.UsersBase
{
    public override async Task<UserResponse> GetUserById(
        GetUserByIdRequest request, ServerCallContext context)
    {
        var userId = Guid.Parse(request.UserId);
        var result = await _sender.Send(new GetUserByIdQuery(userId));
        if (result.IsFailure)
            throw new RpcException(new Status(StatusCode.NotFound, result.Error.Message));

        return new UserResponse
        {
            UserId   = result.Value.Id.ToString(),
            Email    = result.Value.Email,
            FullName = result.Value.FullName
        };
    }

    public override async Task<UserExistsResponse> UserExists(
        UserExistsRequest request, ServerCallContext context)
    {
        var userId = Guid.Parse(request.UserId);
        var result = await _sender.Send(new GetUserByIdQuery(userId));
        return new UserExistsResponse { Exists = result.IsSuccess };
    }
}
```

AuthService expose gRPC trên port **8081** (HTTP/2 only):
```csharp
options.ListenAnyIP(8081, lo => lo.Protocols = HttpProtocols.Http2);
```

### Client (OrderService)
```csharp
// OrderService.Infrastructure/Grpc/AuthUserLookupClient.cs
public class AuthUserLookupClient : IUserLookupService
{
    private readonly Users.UsersClient _grpcClient;

    public async Task<bool> UserExistsAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var response = await _grpcClient.UserExistsAsync(
                new UserExistsRequest { UserId = userId.ToString() }, cancellationToken: ct);
            return response.Exists;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return false;
        }
    }
}

// DI registration (Infrastructure/DependencyInjection.cs):
services.AddGrpcClient<Users.UsersClient>(o =>
    o.Address = new Uri(configuration["Grpc:AuthServiceUrl"]!));
// Giá trị: "http://authservice:8081" trong docker
```

**HTTP/2 cleartext (h2c):** gRPC nội bộ dùng plaintext (không TLS). Cần bật:
```csharp
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
```

---

## RabbitMQ (Asynchronous)

### Topology

```
Exchange: hdos.events  (type: topic, durable: true)
     │
     ├── routing key: UserRegisteredIntegrationEvent
     │        └── Queue: notification.user-registered  → NotificationService
     │
     ├── routing key: UserLoggedInIntegrationEvent
     │        └── Queue: notification.user-logged-in   → NotificationService
     │
     └── routing key: OrderCreatedIntegrationEvent
              └── Queue: notification.order-created    → NotificationService
```

**Lý do dùng topic exchange thay vì direct/fanout:**
- Direct: routing key phải khớp chính xác — cứng nhắc
- Fanout: gửi tới tất cả queue — không control được
- Topic: routing key là pattern (`*.registered`, `order.*`) — linh hoạt

Hiện tại routing key = tên class event (`UserRegisteredIntegrationEvent`). Topic cho phép subscribe theo pattern sau này.

### Publisher
```csharp
// Sử dụng trong handler:
await _eventBus.PublishAsync(new UserRegisteredIntegrationEvent(
    UserId: user.Id,
    Email: user.Email.Value,
    OccurredOn: DateTime.UtcNow));

// IEventBus interface (Application layer):
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IntegrationEvent;
}
```

Phía dưới `RabbitMqEventBus`:
1. Mở channel (reuse connection)
2. Declare exchange (idempotent — không fail nếu đã tồn tại)
3. Serialize event → JSON
4. Set AMQP properties: `Persistent=true` (survive broker restart), `MessageId`, `Type`, `ContentType`
5. Inject W3C trace context vào headers (xem [09 — W3C Trace Context](./09-w3c-trace-context.md))
6. `BasicPublish(exchange, routingKey, body)`

### Consumer
```csharp
// NotificationService.Infrastructure/Consumers/UserRegisteredConsumer.cs
public class UserRegisteredConsumer : IIntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    public async Task HandleAsync(UserRegisteredIntegrationEvent @event, CancellationToken ct)
    {
        var notification = Notification.Create(
            userId: @event.UserId,
            message: $"Chào mừng {@@event.Email} đến Hdos!");

        await _repo.AddAsync(notification, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        await _pusher.PushAsync(@event.UserId.ToString(), notification, ct); // SignalR
    }
}

// Hosted service để đăng ký consumer:
public class UserRegisteredConsumerService
    : RabbitMqConsumerHostedService<UserRegisteredIntegrationEvent, UserRegisteredConsumer>
{
    // Constructor inject connection, queue name, etc.
    // Base class xử lý toàn bộ plumbing
}
```

### Retry strategy

```
Message arrive
     │
     ▼
HandleAsync()
  ┌──────────────────────┐
  │ Success              │ → BasicAck (xóa khỏi queue)
  └──────────────────────┘
  ┌──────────────────────┐
  │ Exception (lần 1)   │ → BasicNack(requeue: true) → RabbitMQ requeue
  └──────────────────────┘
  ┌──────────────────────┐
  │ Exception (lần 2)   │ → BasicNack(requeue: false) → message bị drop
  └──────────────────────┘
  (ea.Redelivered = true nếu đã requeue 1 lần)
```

**Lưu ý production:** Nên thêm Dead Letter Exchange để không mất message lần 2.

### IntegrationEvent base class
```csharp
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
```

---

## Sơ đồ giao tiếp đầy đủ

```
                    ┌─────────────┐
                    │   nginx     │
                    └──────┬──────┘
          ┌────────────────┼────────────────┐
          │                │                │
          ▼                ▼                ▼
   ┌─────────────┐  ┌─────────────┐  ┌─────────────┐
   │ AuthService │  │OrderService │  │  M01Service  │
   │  :8080      │  │  :8080      │  │  :8080       │
   │  :8081(gRPC)│  │             │  │              │
   └──────┬──────┘  └──────┬──────┘  └─────────────┘
          │                │ gRPC:8081
          │          ┌─────┘ (UserExists?)
          │          │
          │    ┌─────────────┐
          │    │  RabbitMQ   │
          │    │  hdos.events│
          │    └──────┬──────┘
          │           │
          │    ┌──────▼──────┐
          │    │Notification │
          │    │  Service    │
          └────┤  :8080      │
(UserRegistered │             │
 UserLoggedIn)  └─────────────┘
```
