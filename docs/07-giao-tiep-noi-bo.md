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

Hệ thống dùng **MassTransit 8.2** làm abstraction layer trên RabbitMQ. Tất cả services dùng `IEventBus` để publish, `IConsumer<T>` để consume — không dùng RabbitMQ.Client trực tiếp.

MassTransit tạo **2 exchange** cho mỗi consumer: message-type exchange (theo namespace) và endpoint exchange (kebab-case từ tên consumer). Retry exponential backoff 5 lần, sau đó vào dead-letter queue `{name}_error`.

**Tài liệu chi tiết:**

| Chủ đề | Doc |
|---|---|
| Topology, naming, cách thêm event mới, test E2E, tất cả events | [17 — MassTransit Messaging](./17-masstransit-messaging.md) |
| Đảm bảo event không mất (Outbox) | [21 — Transactional Outbox Pattern](./21-outbox-pattern.md) |
| Nhận messages từ hệ thống bên ngoài | [27 — External Consumer Pattern](./27-external-consumer-pattern.md) |

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
