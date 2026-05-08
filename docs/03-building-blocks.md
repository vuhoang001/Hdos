# 03 — Building Blocks

Ba project trong `src/BuildingBlocks/` được dùng chung bởi mọi service. **Không
service nào reference service khác** — mọi thứ chia sẻ phải đi qua Building
Blocks (hoặc qua mạng: gRPC / RabbitMQ).

## 1. SharedKernel

Vị trí: `src/BuildingBlocks/SharedKernel/`
Namespace: `Hdos.SharedKernel`
Chỉ chứa **DDD primitives** (không EF, không HTTP, không Rabbit).

| File              | Vai trò                                                                               |
|-------------------|---------------------------------------------------------------------------------------|
| `BaseEntity.cs`   | `BaseEntity<TId>` — `Id`, `CreatedAtUtc`, `UpdatedAtUtc`. Equality theo `Id`.         |
| `AggregateRoot.cs`| Kế thừa BaseEntity, thêm `DomainEvents` collection + `RaiseDomainEvent(...)`.         |
| `IDomainEvent.cs` | Marker `IDomainEvent : INotification` (MediatR). `DomainEvent` base record.           |
| `ValueObject.cs`  | Equality so sánh từng component (override `GetEqualityComponents()`).                 |
| `Result.cs`       | `Result`, `Result<T>`, `Error`. Pattern thay vì throw exception cho expected failures. |

**Dùng khi nào**:

- `BaseEntity<TId>` cho mọi entity có id.
- `AggregateRoot<TId>` cho **gốc** của aggregate — nơi raise domain event.
- `ValueObject` cho concept không có id (`Email`, `Money`).
- `Result<T>` cho handler khi failure là **kỳ vọng** (validation, not-found,
  business rule). Ngoại lệ chỉ dùng cho lỗi không mong đợi.

## 2. Contracts

Vị trí: `src/BuildingBlocks/Contracts/`
Namespace: `Hdos.Contracts.*`
Chứa **mọi thứ đi qua biên giới service**.

```
Contracts/
├── IntegrationEvents/
│   ├── IntegrationEvent.cs
│   ├── UserRegisteredIntegrationEvent.cs
│   ├── UserLoggedInIntegrationEvent.cs
│   └── OrderCreatedIntegrationEvent.cs
└── Protos/
    └── users.proto      ← gRPC contract
```

### 2.1 IntegrationEvent

`IntegrationEvent` (record, base) — có `EventId` (Guid), `OccurredAtUtc`.

Quy tắc viết integration event:

- **Phẳng**, JSON-serializable, không reference Domain entity.
- Dùng kiểu nguyên thuỷ + collection của primitive/record.
- Tên class trùng routing key trong RabbitMQ — đừng đổi tên class một cách tuỳ tiện.
- Phiên bản hoá bằng cách *tạo class mới* (`OrderCreatedIntegrationEventV2`),
  không thêm field "không bắt buộc" rồi giả vờ vẫn tương thích.

### 2.2 Protos

File `.proto` được build với `Grpc.Tools` (đã thêm vào csproj). Khi build:

- Server-side base class + client class được sinh ra trong namespace
  `Hdos.Contracts.Grpc.Users`.
- Cả AuthService.API (server) và OrderService.Infrastructure (client) đều
  reference `Contracts` → cùng dùng class generated, đồng bộ tự nhiên.

Chi tiết xem [07 — gRPC](./07-grpc.md).

## 3. Common

Vị trí: `src/BuildingBlocks/Common/`
Namespace: `Hdos.Common.*`
Chứa **infrastructure cross-cutting**: middleware, MediatR behavior, event bus,
logging config, response wrapper.

### 3.1 Logging — `Logging/SerilogConfig.cs`

```csharp
builder.UseHdosLogging("AuthService"); // tag mọi log với serviceName
```

Console sink, structured logging.

### 3.2 Middleware — `Middleware/`

| Middleware                        | Chức năng                                                                       |
|-----------------------------------|---------------------------------------------------------------------------------|
| `RequestLoggingMiddleware`        | Log mỗi HTTP request: method, path, status, elapsed ms.                          |
| `ExceptionHandlingMiddleware`     | Catch all → map sang `ApiResponse.Fail(...)` JSON.                                |

Map exception → status code:

| Exception                       | Status      | Code         |
|---------------------------------|-------------|--------------|
| `ValidationException`           | 400         | `Validation` |
| `NotFoundException`             | 404         | `NotFound`   |
| `ConflictException`             | 409         | `Conflict`   |
| `UnauthorizedAccessException`   | 401         | `Unauthorized` |
| (mọi exception khác)            | 500         | `Server`     |

Bật trong `Program.cs` qua extension:

```csharp
app.UseHdosMiddleware();   // thứ tự: ExceptionHandling → RequestLogging
```

### 3.3 Response wrapper — `Responses/ApiResponse.cs`

```csharp
ApiResponse<UserDto>.Ok(userDto);
ApiResponse<UserDto>.Fail("NotFound", "User was not found");
```

Mọi REST endpoint của hệ thống đều trả `ApiResponse` hoặc `ApiResponse<T>` để
client có shape thống nhất:

```json
{
  "success": true,
  "data": { ... },
  "errorCode": null,
  "errorMessage": null
}
```

### 3.4 MediatR pipeline — `Behaviors/`

| Behavior              | Vai trò                                                                                |
|-----------------------|----------------------------------------------------------------------------------------|
| `LoggingBehavior`     | Log "Handling X" / "Handled X in Yms" cho mọi request đi qua MediatR.                  |
| `ValidationBehavior`  | Lấy mọi `IValidator<TRequest>`, chạy song song. Có lỗi → throw `ValidationException`.  |

Đăng ký một lần ở Application của mỗi service (xem `AuthService.Application/DependencyInjection.cs`):

```csharp
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(thisAssembly));
services.AddCommonMediatRBehaviors();         // 2 behavior trên
services.AddValidatorsFromAssembly(thisAssembly);
```

### 3.5 Domain Event Dispatcher — `Persistence/`

| File                                       | Vai trò                                                                                              |
|--------------------------------------------|------------------------------------------------------------------------------------------------------|
| `PublishDomainEventsInterceptor.cs`        | EF Core `SaveChangesInterceptor` — sau commit, lấy mọi `IDomainEvent` từ aggregate đang track và publish qua MediatR `IPublisher`. |
| `LoggingDomainEventHandler<TEvent>`        | Open-generic `INotificationHandler<TEvent>` — log bất kỳ domain event nào để debug.                   |

Gắn vào service:

```csharp
services.AddDomainEventDispatching();   // interceptor + open-generic logger

services.AddDbContext<TContext>((sp, opts) =>
    opts.UseSqlServer(...)
        .AddInterceptors(sp.GetRequiredService<PublishDomainEventsInterceptor>()));
```

Sau đó mọi `aggregate.RaiseDomainEvent(...)` rồi `SaveChangesAsync(...)` sẽ
fire event qua MediatR. Handler cụ thể đặt trong `*.Application/EventHandlers/`,
auto-discovered nhờ `RegisterServicesFromAssembly`. Chi tiết xem
[11 — Domain Event Dispatcher](./11-domain-events.md).

### 3.6 Messaging — `Messaging/`

Tách thành 5 file:

| File                             | Vai trò                                                                                  |
|----------------------------------|------------------------------------------------------------------------------------------|
| `RabbitMqOptions.cs`             | Section `RabbitMq` trong appsettings — Host/Port/User/VHost/Exchange/RetryCount.         |
| `RabbitMqConnection.cs`          | Singleton connection factory với retry-on-connect (Polly-style).                         |
| `IEventBus.cs`                   | `IEventBus.PublishAsync<TEvent>(...)` + `IIntegrationEventHandler<TEvent>`.              |
| `RabbitMqEventBus.cs`            | Implementation publisher: declare topic exchange, routingKey = tên class event.          |
| `RabbitMqConsumerHostedService.cs`| Generic `BackgroundService` — declare exchange/queue/binding, manual ack/nack, requeue 1 lần. |

Đăng ký:

```csharp
services.AddRabbitMq(configuration);   // singleton connection + IEventBus
// và cho mỗi consumer service:
services.AddHostedService<UserLoggedInConsumer>();
```

Chi tiết flow xem [08 — RabbitMQ](./08-rabbitmq.md).

## 4. Vì sao tách 3 project

| Project        | Reference được phép từ                                                                       |
|----------------|----------------------------------------------------------------------------------------------|
| `SharedKernel` | Bất kỳ project nào (rất ít phụ thuộc, không kéo theo gì nặng).                               |
| `Contracts`    | Chỉ `Application` & `Infrastructure` & `API` của các service. Không reference từ `Domain`.   |
| `Common`       | `Application` (cho Behaviors), `Infrastructure` (cho RabbitMQ), `API` (middleware). Không reference từ `Domain`. |

Nếu để chung 1 project, `Domain` sẽ vô tình kéo theo MediatR, FluentValidation,
RabbitMQ.Client — phá vỡ quy tắc "Domain pure". Tách ra để compiler tự bắt
nếu lỡ tay reference sai.
