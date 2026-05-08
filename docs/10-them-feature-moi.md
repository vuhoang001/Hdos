# 10 — Thêm feature / service mới

Tài liệu này gom checklist các thay đổi thực tế cần làm cho 4 tình huống phổ
biến nhất.

## A. Thêm 1 endpoint REST mới (trong service đã có)

Ví dụ: thêm `POST /orders/{id}/cancel` vào `OrderService`.

1. **Domain** — bổ sung hành vi vào aggregate (nếu chưa có):
   ```csharp
   // Order.cs đã có sẵn:
   public void Cancel() { Status = OrderStatus.Cancelled; UpdatedAtUtc = DateTime.UtcNow; }
   ```

2. **Application** — tạo folder feature mới:
   ```
   Features/CancelOrder/CancelOrderCommand.cs
   ```
   File chứa `CancelOrderCommand` (record), `CancelOrderCommandValidator`,
   `CancelOrderCommandHandler`. Xem mẫu trong
   `Features/CreateOrder/CreateOrderCommand.cs`.

3. **API** — thêm action trong controller:
   ```csharp
   [HttpPost("{id:guid}/cancel")]
   public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
   {
       var result = await _sender.Send(new CancelOrderCommand(id), ct);
       return result.IsSuccess
           ? Ok(ApiResponse<OrderDto>.Ok(result.Value))
           : NotFound(ApiResponse<OrderDto>.Fail(result.Error.Code, result.Error.Message));
   }
   ```

4. **Validator** được FluentValidation auto-discover (đăng ký theo assembly).
5. **Test** local: `curl -X POST http://localhost:5000/orders/<id>/cancel`.

Không cần đụng `Infrastructure` nếu repo đã có method bạn cần. Không cần đụng
`Gateway` (path `/orders/*` đã được route).

## B. Phát một integration event mới

Ví dụ: phát `OrderCancelledIntegrationEvent` khi cancel.

1. **Contracts** — tạo class event:
   ```csharp
   // src/BuildingBlocks/Contracts/IntegrationEvents/OrderCancelledIntegrationEvent.cs
   namespace Hdos.Contracts.IntegrationEvents;
   public sealed record OrderCancelledIntegrationEvent(
       Guid OrderId, Guid CustomerId, string CustomerEmail) : IntegrationEvent;
   ```

2. **Application** — handler publish trong CancelOrderCommandHandler:
   ```csharp
   await _eventBus.PublishAsync(
       new OrderCancelledIntegrationEvent(order.Id, order.CustomerId, order.CustomerEmail), ct);
   ```

3. **Subscriber** — service nào cần nghe:
   - Tạo handler `OrderCancelledEventHandler : IIntegrationEventHandler<OrderCancelledIntegrationEvent>`
     trong `*.Application/EventHandlers/`.
   - Tạo consumer:
     ```csharp
     public sealed class OrderCancelledConsumer
         : RabbitMqConsumerHostedService<OrderCancelledIntegrationEvent, OrderCancelledEventHandler>
     {
         public OrderCancelledConsumer(...) : base(..., queueName: "<svc>.order-cancelled") { }
     }
     ```
   - Đăng ký:
     ```csharp
     services.AddScoped<OrderCancelledEventHandler>();   // Application DI
     services.AddHostedService<OrderCancelledConsumer>(); // Infrastructure DI
     ```

Chi tiết hơn: [08 — RabbitMQ](./08-rabbitmq.md).

## C. Thêm RPC mới qua gRPC

Ví dụ: thêm `OrderService` expose `OrderService.GetOrderStatus(orderId)` để
service khác gọi.

1. **Contracts** — tạo `Protos/orders.proto`:
   ```proto
   syntax = "proto3";
   option csharp_namespace = "Hdos.Contracts.Grpc.Orders";
   package hdos.orders.v1;

   service OrderService {
     rpc GetOrderStatus (GetOrderStatusRequest) returns (OrderStatusReply);
   }
   message GetOrderStatusRequest { string order_id = 1; }
   message OrderStatusReply      { string status   = 1; }
   ```

2. **Contracts.csproj** — thêm:
   ```xml
   <Protobuf Include="Protos\orders.proto" GrpcServices="Both" />
   ```

3. **Server (OrderService.API)**:
   - Add `Grpc.AspNetCore` package nếu chưa có.
   - Mở Program.cs, đảm bảo Kestrel listen 2 cổng (xem
     `AuthService.API/Program.cs` làm mẫu).
   - Thêm `services.AddGrpc()`.
   - Tạo `Grpc/OrderGrpcService.cs` kế thừa `OrderService.OrderServiceBase`,
     override `GetOrderStatus`.
   - `app.MapGrpcService<OrderGrpcService>();`.
   - Thêm config `Kestrel:GrpcPort` cho dev và env var trong compose.

4. **Client (service gọi tới)**:
   - Application: định nghĩa interface port (vd `IOrderStatusLookup`).
   - Infrastructure:
     ```csharp
     services.AddGrpcClient<OrderService.OrderServiceClient>(o =>
         o.Address = new Uri(configuration["Services:Order:GrpcUrl"]!));
     services.AddScoped<IOrderStatusLookup, OrderStatusGrpcAdapter>();
     ```
   - API Program.cs nếu lần đầu service này gọi gRPC, thêm:
     ```csharp
     AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
     ```

Chi tiết: [07 — gRPC](./07-grpc.md).

## D. Thêm 1 service hoàn toàn mới (vd `PaymentService`)

1. **Tạo 4 project** theo template:
   ```
   src/Services/PaymentService/
     PaymentService.Domain/
     PaymentService.Application/
     PaymentService.Infrastructure/
     PaymentService.API/
   ```
   Cách nhanh: copy từ `OrderService/`, find-replace `OrderService` → `PaymentService`,
   `Hdos.OrderService` → `Hdos.PaymentService`, `Order` → `Payment` (cẩn thận
   không thay nhầm `OrderService` mà bạn vẫn cần để gọi gRPC).

2. **Solution** — thêm 4 project vào `Hdos.sln`:
   ```bash
   dotnet sln add src/Services/PaymentService/PaymentService.Domain/PaymentService.Domain.csproj
   dotnet sln add src/Services/PaymentService/PaymentService.Application/PaymentService.Application.csproj
   dotnet sln add src/Services/PaymentService/PaymentService.Infrastructure/PaymentService.Infrastructure.csproj
   dotnet sln add src/Services/PaymentService/PaymentService.API/PaymentService.API.csproj
   ```
   (Hoặc edit `Hdos.sln` thủ công, copy block tương tự `OrderService`.)

3. **Project references**:
   - `Application` → `Domain`, `BuildingBlocks/Common`, `BuildingBlocks/Contracts`.
   - `Infrastructure` → `Application` (+ `Contracts` nếu cần gRPC client).
   - `API` → `Application`, `Infrastructure`, `BuildingBlocks/Common`,
     `BuildingBlocks/Contracts`.

4. **Application/Infrastructure DI**:
   - `AddPaymentApplication()` — MediatR + FluentValidation + behaviors.
   - `AddPaymentInfrastructure(IConfiguration)` — DbContext + repos +
     `AddRabbitMq(...)` + (tuỳ) gRPC clients.

5. **API Program.cs** — copy từ service tương tự, đảm bảo:
   - `UseHdosLogging("PaymentService")`.
   - DbContext migration retry loop ở cuối.

6. **Database** — connection string `PaymentDb` trong `appsettings.json`,
   thêm vào docker-compose env. EF Core tự `MigrateAsync` lúc startup nếu có
   migration. Chạy:
   ```bash
   dotnet ef migrations add Init \
     --project src/Services/PaymentService/PaymentService.Infrastructure \
     --startup-project src/Services/PaymentService/PaymentService.API \
     -o Persistence/Migrations
   ```

7. **docker-compose.yml** — thêm service block:
   ```yaml
   paymentservice:
     image: hdos/paymentservice:latest
     build:
       context: .
       dockerfile: src/Services/PaymentService/PaymentService.API/Dockerfile
     environment:
       ASPNETCORE_ENVIRONMENT: Development
       ConnectionStrings__PaymentDb: "Server=sqlserver,1433;Database=PaymentDb;User Id=sa;Password=Hdos!Pass123;TrustServerCertificate=True;Encrypt=False"
       RabbitMq__Host: rabbitmq
     depends_on:
       sqlserver: { condition: service_healthy }
       rabbitmq:  { condition: service_healthy }
     ports: [ "5104:8080" ]
     networks: [hdos-net]
   ```

8. **Dockerfile** — copy từ OrderService, thay tên project + DLL.

9. **API Gateway** — `appsettings.json` + `appsettings.Docker.json`:
   ```json
   "payments-route":   { "ClusterId": "payments-cluster", "Match": { "Path": "/payments/{**catch-all}" } },
   ...
   "payments-cluster": { "Destinations": { "payments-1": { "Address": "http://localhost:5104/" } } }
   ```

10. **Smoke test**:
    ```bash
    docker compose up --build paymentservice
    curl http://localhost:5000/payments/health
    ```

## E. Đổi cấu hình port / URL

| Cần làm                     | Sửa file                                                            |
|-----------------------------|---------------------------------------------------------------------|
| Đổi REST port AuthService   | `AuthService.API/appsettings.Development.json`: `Kestrel:RestPort`. |
| Đổi gRPC port AuthService   | `AuthService.API/appsettings.Development.json`: `Kestrel:GrpcPort`. |
| Đổi URL gRPC client         | OrderService `appsettings.json`: `Services:Auth:GrpcUrl`.            |
| Trong Docker                | env var trong `docker-compose.yml` (ưu tiên hơn appsettings).        |
| Đổi gateway port            | YARP listen từ `ASPNETCORE_URLS` / Kestrel section.                  |

## F. Style guide nhanh

- Endpoint trả `ApiResponse<T>` — đừng trả entity / Domain object thẳng.
- Failure dự kiến → `Result<T>`, không throw.
- Lỗi không mong đợi → throw, để `ExceptionHandlingMiddleware` xử lý.
- Validate input ở `*Validator : AbstractValidator<T>` — không trộn validate
  vào handler.
- Một feature = một folder trong `Features/`. Một file gom Command + Validator
  + Handler nếu nhỏ; tách file khi lớn dần.
- Domain event = nội bộ; Integration event = qua RabbitMQ. Không nhầm.
- Mọi DTO ra ngoài (REST hay gRPC) đều ở `*.Application/DTOs/` hoặc
  `BuildingBlocks/Contracts/`.
