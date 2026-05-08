# 05 — Feature: OrderService

`OrderService` quản lý đơn hàng. Có 2 use case REST + tiêu thụ 1 service ngoài
qua **gRPC** + publish 1 integration event qua **RabbitMQ**.

## 1. Endpoints

| Method | Path                | Use case             | MediatR request          |
|--------|---------------------|----------------------|--------------------------|
| POST   | `/orders`           | Tạo đơn hàng          | `CreateOrderCommand`     |
| GET    | `/orders/{id}`      | Lấy đơn theo id       | `GetOrderByIdQuery`      |
| GET    | `/orders/health`    | Health check          | (controller trực tiếp)   |

## 2. Domain (`OrderService.Domain`)

### Aggregate `Order`

File: `src/Services/OrderService/OrderService.Domain/Entities/Order.cs`

Đặc điểm:

- `Order : AggregateRoot<Guid>` — gốc của aggregate.
- `OrderItem` là entity con (id riêng), nhưng truy cập **chỉ qua** `Order` —
  không có `IOrderItemRepository`.
- `Money` (Value Object) gắn currency để chặn cộng VND + USD.
- Factory `Order.Create(...)` validate non-empty items, tự tính `Total`, raise
  `OrderCreatedDomainEvent`.

```csharp
public static Order Create(Guid customerId, string customerEmail,
    IEnumerable<(string product, int qty, decimal unit, string currency)> lines)
{
    if (string.IsNullOrWhiteSpace(customerEmail))
        throw new ArgumentException("Customer email required", nameof(customerEmail));

    var order = new Order { Id = Guid.NewGuid(), CustomerId = customerId,
        CustomerEmail = customerEmail.Trim().ToLowerInvariant(),
        Status = OrderStatus.Pending, Total = Money.Zero() };

    var any = false;
    foreach (var line in lines)
    {
        order._items.Add(new OrderItem(order.Id, line.product, line.qty, Money.Of(line.unit, line.currency)));
        any = true;
    }
    if (!any) throw new InvalidOperationException("Order must contain at least one item.");

    order.Total = order._items.Aggregate(Money.Zero(order._items[0].UnitPrice.Currency),
        (acc, item) => acc.Add(item.LineTotal));

    order.RaiseDomainEvent(new OrderCreatedDomainEvent(order.Id, order.CustomerId, order.Total.Amount));
    return order;
}
```

## 3. Use case `CreateOrder` — kết hợp gRPC + RabbitMQ

Đây là **feature trung tâm** của ví dụ này: nó dùng cả gRPC (sync, gọi sang
AuthService) **và** RabbitMQ (async, publish cho NotificationService).

```
[Client] POST /orders {customerId, items}
         │
         ▼
[OrdersController] → ISender.Send(cmd)
         │
         ▼
[MediatR pipeline] LoggingBehavior → ValidationBehavior
         │
         ▼
[CreateOrderCommandHandler]
   │
   │ 1. Verify user qua gRPC
   │   ┌──────────────────────────────────────────┐
   │   │ IUserLookupService.GetByIdAsync(id)      │
   │   │   ↳ AuthUserLookupClient (Infrastructure)│
   │   │     ↳ UserService.UserServiceClient      │ ← Grpc.Net.ClientFactory
   │   │       ↳ HTTP/2 → AuthService :5111       │
   │   │         ↳ UserGrpcService.GetUserById    │
   │   │           ↳ IUserRepository.GetByIdAsync │
   │   │           → UserReply / RpcException(NotFound)│
   │   └──────────────────────────────────────────┘
   │   Result<UserLookupDto>:
   │     - Failure(NotFound) → trả 400 cho client (không tạo order)
   │     - Success(user)     → đi tiếp, dùng user.Email làm CustomerEmail
   │
   │ 2. Tạo aggregate
   │   var order = Order.Create(customerId, lookup.Value.Email, lines);
   │
   │ 3. Persist
   │   _orders.AddAsync(order)
   │   _uow.SaveChangesAsync()      ← EF Core commit
   │
   │ 4. Publish integration event
   │   _eventBus.PublishAsync(new OrderCreatedIntegrationEvent(...))
   │     ↳ RabbitMqEventBus → exchange "hdos.events" routingKey "OrderCreatedIntegrationEvent"
   │       ↳ NotificationService consumer "notification.order-created" sẽ nhận và lưu
   │
   ▼
ApiResponse<OrderDto>.Ok(...)
```

File: `Application/Features/CreateOrder/CreateOrderCommand.cs`

```csharp
public async Task<Result<OrderDto>> Handle(CreateOrderCommand request, CancellationToken ct)
{
    // Cross-service sync call
    var lookup = await _users.GetByIdAsync(request.CustomerId, ct);
    if (lookup.IsFailure) return Result.Failure<OrderDto>(lookup.Error);

    var order = Order.Create(request.CustomerId, lookup.Value.Email,
        request.Items.Select(i => (i.ProductName, i.Quantity, i.UnitPrice, i.Currency)));

    await _orders.AddAsync(order, ct);
    await _uow.SaveChangesAsync(ct);

    await _eventBus.PublishAsync(
        new Integration.OrderCreatedIntegrationEvent(
            order.Id, order.CustomerId, order.CustomerEmail,
            order.Total.Amount, integrationItems), ct);

    return Map(order);
}
```

### Vì sao gRPC chứ không HTTP?

| Tiêu chí       | gRPC                                            | HTTP/JSON                              |
|----------------|--------------------------------------------------|----------------------------------------|
| Hợp đồng       | `.proto` strongly-typed, sinh code 2 chiều       | OpenAPI / share DTO thủ công           |
| Encoding       | Binary (Protobuf)                                | JSON text                              |
| Multiplex      | HTTP/2, nhiều RPC/stream trên 1 connection       | HTTP/1.1 mỗi request 1 connection      |
| Streaming      | Native (server/client/bi-di)                     | Phải SSE / WebSocket                   |
| Tooling .NET   | `Grpc.Tools` sinh class tự động                  | `RestClient` thủ công hoặc Refit       |

Service-to-service nội bộ thường chọn gRPC vì *strict contract* + nhanh hơn JSON.

### Vì sao RabbitMQ chứ không gRPC luôn?

Order tạo xong, NotificationService có đọc hay không **không ảnh hưởng** kết
quả trả về client. Nếu dùng gRPC tới Notification ở đây thì:

- OrderService chết khi NotificationService down → coupling chặt.
- Phải retry, timeout, circuit breaker — cùng một lý do người ta phát minh ra
  message bus.

→ Đẩy ra Rabbit cho NotificationService tự consume khi nó sẵn sàng. Nếu Notif
down lúc Order publish, message vẫn nằm trong queue chờ.

## 4. Use case `GetOrderById`

File: `Application/Features/GetOrder/GetOrderByIdQuery.cs` — straightforward,
chỉ là read-only query không gọi service ngoài.

## 5. Application abstractions

File: `Application/Abstractions/IUserLookupService.cs`

```csharp
public sealed record UserLookupDto(Guid Id, string Email, string FullName);

public interface IUserLookupService
{
    Task<Result<UserLookupDto>> GetByIdAsync(Guid userId, CancellationToken ct);
}
```

Đây là **port** — Application chỉ biết tới interface, không biết là gRPC. Nếu
sau này cache user trong Redis hoặc fall-back sang HTTP, chỉ thay implementation
ở Infrastructure, code Application không đổi.

## 6. Infrastructure (`OrderService.Infrastructure`)

| File                                          | Vai trò                                                                  |
|-----------------------------------------------|--------------------------------------------------------------------------|
| `Persistence/OrderDbContext.cs`               | EF DbContext + audit `SaveChangesAsync`.                                 |
| `Persistence/Configurations/OrderConfiguration.cs` | Map `Order`, `OrderItem`, owned `Money`.                            |
| `Persistence/OrderRepository.cs`              | `IOrderRepository` + `IUnitOfWork` impl, `Include(o => o.Items)`.        |
| `Grpc/AuthUserLookupClient.cs`                | Adapter `IUserLookupService` → `UserService.UserServiceClient` (gRPC).   |
| `DependencyInjection.cs`                      | `AddOrderInfrastructure(IConfiguration)`.                                |

`DependencyInjection.cs` đăng ký gRPC client qua `Grpc.Net.ClientFactory`:

```csharp
var authGrpcUrl = configuration["Services:Auth:GrpcUrl"] ?? "http://localhost:5111";
services.AddGrpcClient<UserService.UserServiceClient>(o => { o.Address = new Uri(authGrpcUrl); });
services.AddScoped<IUserLookupService, AuthUserLookupClient>();
```

Adapter (`AuthUserLookupClient`) chuyển `RpcException(NotFound)` → `Result.Failure(Error.NotFound)`,
chuyển lỗi gRPC khác → `Error("User.GrpcError", ...)`. Application không thấy
`RpcException` bao giờ.

## 7. API (`OrderService.API`)

`Program.cs` cần một dòng đặc biệt cho gRPC client over plain HTTP:

```csharp
// Cho phép client gọi HTTP/2 trên scheme http:// (không TLS)
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
```

Nếu chạy HTTPS thật thì không cần cờ này.

## 8. Cấu hình

`appsettings.json`:

```json
{
  "Services": {
    "Auth": { "GrpcUrl": "http://localhost:5111" }
  }
}
```

Docker compose override:

```yaml
environment:
  Services__Auth__GrpcUrl: "http://authservice:8081"
```
