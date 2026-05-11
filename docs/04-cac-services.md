# 04 — Các Services

---

## AuthService

**Trách nhiệm:** Quản lý danh tính người dùng — đăng ký, đăng nhập, cấp JWT, xác thực token.

### Domain
- **User** (AggregateRoot): có `Email` (ValueObject), `PasswordHash`
- **Email** (ValueObject): validate format, equality by value
- `UserRegisteredDomainEvent`: raise khi user đăng ký thành công
- `UserLoggedInDomainEvent`: raise khi login thành công

### Endpoints
| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| POST | `/auth/register` | Anonymous | Đăng ký user mới |
| POST | `/auth/login` | Anonymous | Đăng nhập, trả JWT |
| GET | `/auth/users/{id}` | JWT | Lấy thông tin user |
| GET | `/auth/validate` | JWT | Validate token (dùng bởi nginx auth_request) |
| GET | `/auth/health` | Anonymous | Health check |

### Kestrel hai port
```csharp
options.ListenAnyIP(8080, lo => lo.Protocols = HttpProtocols.Http1AndHttp2); // REST
options.ListenAnyIP(8081, lo => lo.Protocols = HttpProtocols.Http2);          // gRPC only
```

**Lý do tách port:** gRPC cần HTTP/2. Swashbuckle (Swagger) không tương thích tốt với HTTP/2. Tách port đảm bảo Swagger hoạt động trên 8080 (HTTP/1.1) trong khi gRPC hoạt động riêng trên 8081.

### gRPC Service
`UserGrpcService` implement `Users.UsersBase` (generated từ `users.proto`):
- `GetUserById(request)` → trả `UserResponse` hoặc throw `RpcException(NotFound)`
- `UserExists(request)` → trả `UserExistsResponse{exists: bool}`

Xem chi tiết [07 — Giao tiếp nội bộ](./07-giao-tiep-noi-bo.md).

### Luồng đăng ký
```
POST /auth/register
  → RegisterUserCommand (FluentValidation chạy tự động)
  → RegisterUserCommandHandler
      → Check email đã tồn tại chưa (IUserRepository)
      → User.Create(email, passwordHash)    ← raise UserRegisteredDomainEvent
      → userRepository.Add(user)
      → SaveChangesAsync()
          → PublishDomainEventsInterceptor
              → UserRegisteredDomainEventHandler
                  → eventBus.Publish(UserRegisteredIntegrationEvent)
                      → RabbitMQ → NotificationService consume
  ← 200 { userId, email, createdAt }
```

---

## OrderService

**Trách nhiệm:** Quản lý đặt khám / phiếu khám. Cần verify user hợp lệ trước khi tạo order.

### Domain
- **Order** (AggregateRoot): có `UserId`, `Items` (list OrderItem), `Status`
- **OrderItem** (Entity): `ProductName`, `Price` (Money ValueObject), `Quantity`
- **Money** (ValueObject): `Amount`, `Currency`
- `OrderCreatedDomainEvent`: raise khi order tạo thành công

### Endpoints
| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| POST | `/orders/` | JWT | Tạo order mới |
| GET | `/orders/{id}` | JWT | Lấy order theo ID |
| GET | `/orders/health/live` | Anonymous | Health |

### Dependency quan trọng: `IUserLookupService`

```csharp
// Application layer (interface — không biết gRPC):
public interface IUserLookupService
{
    Task<bool> UserExistsAsync(Guid userId, CancellationToken ct);
}

// Infrastructure layer (implementation dùng gRPC):
public class AuthUserLookupClient : IUserLookupService
{
    private readonly Users.UsersClient _grpcClient;

    public async Task<bool> UserExistsAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var response = await _grpcClient.UserExistsAsync(
                new UserExistsRequest { UserId = userId.ToString() });
            return response.Exists;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return false;
        }
    }
}
```

**Tại sao dùng interface?** Nếu sau này không dùng gRPC nữa (đổi sang HTTP hay event-based lookup), chỉ cần đổi implementation trong Infrastructure. Application không đổi gì.

### Luồng tạo order
```
POST /orders/
  → CreateOrderCommand
  → CreateOrderCommandHandler
      → userLookupService.UserExistsAsync(userId)  ← gRPC call sang AuthService
      → Nếu không tồn tại → Result.Failure(OrderErrors.UserNotFound)
      → Order.Create(userId, items)
      → orderRepository.Add(order)
      → SaveChangesAsync() → publish OrderCreatedDomainEvent
          → OrderCreatedDomainEventHandler
              → eventBus.Publish(OrderCreatedIntegrationEvent)
  ← 200 { orderId }
```

---

## NotificationService

**Trách nhiệm:** Lắng nghe events từ RabbitMQ và gửi thông báo real-time qua SignalR.

### Không có gRPC server
NotificationService chỉ consume events, không expose gRPC. Không cần.

### Consumers
| Consumer | Event | Hành động |
|---------|-------|-----------|
| `UserRegisteredConsumer` | `UserRegisteredIntegrationEvent` | Lưu notification "Chào mừng bạn đến Hdos" |
| `UserLoggedInConsumer` | `UserLoggedInIntegrationEvent` | Lưu notification "Đăng nhập lúc {time}" |
| `OrderCreatedConsumer` | `OrderCreatedIntegrationEvent` | Lưu notification "Order #{id} đã được tạo" |

Sau khi lưu, mỗi consumer gọi `INotificationPusher.PushAsync(userId, notification)` để push real-time.

### SignalR Hub
```
/notifications/hubs/notifications   ← WebSocket endpoint
```

`EmailUserIdProvider` override mặc định — dùng claim `email` làm userId thay vì `sub`. Điều này cho phép push tới đúng connection của user bằng email.

```csharp
// Push notification tới user cụ thể:
await _hubContext.Clients.User(userEmail)
    .SendAsync("notification", notificationDto);
```

### Lưu ý scale SignalR
Nếu chạy nhiều replica NotificationService: cần **Redis backplane**. Không thì notification chỉ push tới user kết nối với instance đang xử lý event.

---

## M01Service

**Trách nhiệm:** Module nghiệp vụ M01 — quản lý cấp cứu, phòng khám, dashboard thống kê.

### Endpoints đặc trưng
| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| GET | `/m01/dashboard/summary` | JWT | Tổng quan: lượt khám, thời gian chờ, triage |
| GET | `/m01/cap-cuu` | JWT | Danh sách bệnh nhân cấp cứu |
| GET | `/m01/phong-kham/tai` | JWT | Thông tin phòng khám tai |

### Response mẫu `/m01/dashboard/summary`
```json
{
  "tongLuotKham": 128,
  "choKhamTbPhut": 18,
  "choMaxPhut": 45,
  "triage": { "p1": 2, "p2": 5, "p3": 11 },
  "trongNguong": true,
  "updatedAt": "2026-05-11T06:13:19.922Z"
}
```

---

## Middleware stack của mỗi service

Tất cả services có cùng thứ tự middleware (quan trọng — thứ tự ảnh hưởng đến behavior):

```csharp
// 1. Swagger UI (serve trước để không cần auth)
app.UseSwagger(c => c.RouteTemplate = "{service}/swagger/{doc}/swagger.json");
app.UseSwaggerUI(c => { c.RoutePrefix = "{service}/swagger"; });

// 2. Exception handling — phải đứng đầu để bắt tất cả exception bên dưới
app.UseMiddleware<ExceptionHandlingMiddleware>();

// 3. Request logging — đứng sau exception handler để log cả exception
app.UseMiddleware<RequestLoggingMiddleware>();

// 4. Prometheus metrics — track latency/error rate
app.UseHttpMetrics();

// 5. CORS
app.UseHdosCors();

// 6. Authentication — parse và validate JWT
app.UseAuthentication();

// 7. Authorization — kiểm tra [Authorize] attribute
app.UseAuthorization();

// 8. Controller routes
app.MapControllers();

// 9. gRPC (AuthService only)
app.MapGrpcService<UserGrpcService>();

// 10. /metrics và /health endpoints
app.UseHdosMonitoring();
```

**Lý do thứ tự này:**
- ExceptionHandling phải đứng đầu để bắt lỗi từ mọi middleware phía dưới
- Authentication trước Authorization (phải biết "ai" trước khi quyết định "được làm gì")
- `UseHdosMonitoring()` (MapMetrics, MapHealthChecks) đứng cuối vì chúng là minimal API endpoints, không cần auth
