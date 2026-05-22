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

## RabbitMQ / MassTransit (Asynchronous)

Hệ thống dùng **MassTransit 8.2** làm abstraction layer trên RabbitMQ, không dùng RabbitMQ.Client trực tiếp.

### Topology

MassTransit tạo **một fanout exchange per message type**. Khi consumer được đặt tên theo quy ước, exchange và queue có **cùng tên** và merge thành một entity trong RabbitMQ:

```
Exchange: user-registered [fanout]
     └── Queue: user-registered  →  NotificationService.UserRegisteredConsumer

Exchange: order-created [fanout]
     └── Queue: order-created    →  NotificationService.OrderCreatedConsumer

Exchange: order-create-requested [fanout]
     └── Queue: order-create-requested  →  OrderService.OrderCreateRequestedConsumer
```

Exchange name = tên event bỏ suffix `IntegrationEvent`, kebab-case. Queue name = tên consumer bỏ suffix `Consumer`, kebab-case. Khi đặt tên đúng quy ước, hai tên trùng nhau → RabbitMQ chỉ tạo 1 exchange.

### Publisher

Tất cả services dùng `IEventBus` để publish — interface nằm trong `Common`, không phụ thuộc MassTransit:

```csharp
await eventBus.PublishAsync(new UserRegisteredIntegrationEvent(
    UserId: user.Id,
    Email: user.Email.Value,
    FullName: user.FullName), ct);
```

### Consumer

Mỗi consumer là `IConsumer<T>` (MassTransit), delegate xuống Application handler chứa business logic:

```csharp
// Infrastructure layer
public sealed class UserRegisteredConsumer(UserRegisteredEventHandler handler)
    : IConsumer<UserRegisteredIntegrationEvent>
{
    public Task Consume(ConsumeContext<UserRegisteredIntegrationEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
```

### Retry & Dead-letter

- Retry exponential backoff: tối đa **5 lần**, từ 1s đến 30s
- Sau 5 lần thất bại → message chuyển sang queue `{name}_error` (dead-letter tự động)

**Chi tiết đầy đủ, quy tắc đặt tên, hướng dẫn thêm publisher/consumer mới:** xem [17 — MassTransit Messaging](./17-masstransit-messaging.md).

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
   │ AuthService │  │OrderService │  │ AsyncGateway │
   │  :8080      │  │  :8080      │  │  :8080       │
   │  :8081(gRPC)│  │             │  │              │
   └──────┬──────┘  └──┬──────┬───┘  └──────┬───────┘
          │             │ gRPC │             │
          │         ┌───┘      │             │
          │         ▼          │             │
          │   AuthService      │             │
          │   (UserExists?)    │             │
          │                    │             │
          │    ┌───────────────────────────┐ │
          │    │   RabbitMQ (MassTransit)  │ │
          │    │  Exchange per message type│◄┘
          │    └──────┬────────────────────┘
          │           │
          │    ┌──────▼──────────┐
          └───►│ NotificationSvc │
(UserRegistered│ user-registered │
 UserLoggedIn) │ order-created   │
               └─────────────────┘
```
