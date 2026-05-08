# 11 — Domain Event Dispatcher

Tài liệu này giải thích cơ chế dispatch domain event mới wire vào hệ thống.
Đây là **trong-process** event (chạy cùng request, chung DI scope với DbContext)
— khác hoàn toàn với Integration Event đi qua RabbitMQ ([08](./08-rabbitmq.md)).

## 1. Sự khác nhau Domain ↔ Integration

|                          | Domain Event                              | Integration Event                       |
|--------------------------|-------------------------------------------|-----------------------------------------|
| Phạm vi                  | Trong cùng 1 process (1 service)           | Giữa các service                         |
| Transport                | MediatR `IPublisher` (in-memory)           | RabbitMQ topic exchange                  |
| Khi nào fire              | Tự động sau khi `SaveChanges` thành công   | Handler gọi tay `IEventBus.PublishAsync` |
| Handler thấy lỗi         | Cùng request, status 500 (vì throw bubble) | Consumer retry/drop, publisher không thấy |
| Kiểu dữ liệu             | Tham chiếu domain object được              | Phải JSON-serializable phẳng              |
| Coupling                 | Cùng deploy, cùng schema                   | Loose, evolvable                         |

Quy tắc dùng:

- **Domain event** = "việc nội bộ vừa xảy ra". Ví dụ: log audit, raise sub-task
  trong cùng service, trigger validate cross-aggregate.
- **Integration event** = "thông báo cho thế giới ngoài service". Khi cần
  service khác phản ứng → publish lên Rabbit.

Có thể **kết hợp**: domain event handler (in-process) được gọi rồi nó publish
integration event qua `IEventBus`. Đây là pattern textbook tách "what happened
in my domain" khỏi "tell others about it".

## 2. Cách dispatcher hoạt động

### Sơ đồ tổng

```
[Use case handler]
   user.RaiseDomainEvent(new UserRegisteredDomainEvent(...))
        │
        │  (chỉ Add vào AggregateRoot._domainEvents — chưa ai nghe)
        ▼
   _uow.SaveChangesAsync(ct)
        │   ├── DbContext.SaveChangesAsync
        │   │     ├── EF Core commit transaction
        │   │     └── ── ── ── ── ── ── ── ── ── ── ── ── ──
        │   │                                              ▼
        │   │            [PublishDomainEventsInterceptor.SavedChangesAsync]
        │   │                ChangeTracker.Entries()
        │   │                  → lọc IHasDomainEvents
        │   │                  → snapshot list events, ClearDomainEvents()
        │   │                  → foreach event: IPublisher.Publish(event, ct)
        │   │                                                    │
        │   │                                                    ▼
        │   │                                        MediatR fan-out
        │   │                                        ├── LoggingDomainEventHandler<T>  (open generic, log)
        │   │                                        └── UserRegisteredDomainEventHandler (specific)
        │   │
        │   └── trả về số row affected
        ▼
   tiếp tục handler (vd publish integration event)
```

### Bốn mảnh

| File                                                                    | Vai trò                                                                                                  |
|-------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------|
| `SharedKernel/IHasDomainEvents.cs`                                      | Marker non-generic. `AggregateRoot<TId>` implement → DbContext query không cần biết `TId`.               |
| `Common/Persistence/PublishDomainEventsInterceptor.cs`                  | EF Core `SaveChangesInterceptor`. Override `SavedChangesAsync` (post-save) → publish qua `IPublisher`.    |
| `Common/Persistence/LoggingDomainEventHandler.cs`                       | Open-generic `INotificationHandler<TEvent> where TEvent : IDomainEvent` — log mọi domain event.          |
| `Common/Extensions/ServiceCollectionExtensions.cs:AddDomainEventDispatching` | Đăng ký interceptor (Scoped) + open-generic logger.                                                  |

## 3. Đăng ký trong service

Xem `AuthService.Infrastructure/DependencyInjection.cs`:

```csharp
services.AddDomainEventDispatching();          // 1. interceptor + open-generic logger

services.AddDbContext<AuthDbContext>((sp, opts) =>
    opts.UseSqlServer(connStr, sql => sql.MigrationsAssembly(...))
        .AddInterceptors(sp.GetRequiredService<PublishDomainEventsInterceptor>()));  // 2. attach
```

Hai điểm quan trọng:

- `AddDbContext` dùng overload `(sp, opts) =>` để có `IServiceProvider`, từ đó
  resolve interceptor scoped.
- `AddInterceptors(...)` — EF Core sẽ gọi interceptor mỗi lần SaveChanges trên
  context này.

Application không cần đụng gì. MediatR đã `AddMediatR(...RegisterServicesFromAssembly(thisAssembly))`
trong `AddAuthApplication()` ⇒ mọi `INotificationHandler<TConcrete>` trong
assembly Application tự động được pick lên.

## 4. Vì sao post-save (SavedChangesAsync) chứ không pre-save?

Có 2 chỗ cắm:

| Override                     | Khi gọi                       | Đặc điểm                                                                                       |
|------------------------------|-------------------------------|------------------------------------------------------------------------------------------------|
| `SavingChangesAsync`         | Trước khi commit              | Handler vẫn còn trong tx → có thể mutate entity, được persist cùng tx. Nguy cơ recursion nếu handler gọi `SaveChanges`. |
| `SavedChangesAsync` (chọn)   | Sau khi commit thành công      | Handler không thay đổi state được persist cùng tx. Đơn giản, không recursion. Nếu handler throw → dữ liệu vẫn đã commit. |

Hiện chọn **post-save** vì:

- An toàn (không nesting tx, không recursion).
- Domain event = "fact đã xảy ra rồi" — semantically khớp với việc dispatch
  sau khi đã commit.
- Trong codebase chưa có handler nào cần ghi DB cùng tx với aggregate gốc.

Nếu sau cần pre-save, đổi `SavedChangesAsync` → `SavingChangesAsync` trong
`PublishDomainEventsInterceptor.cs`. Nhớ test recursion + cập nhật doc này.

## 5. Hai handler đã có sẵn (để bạn nhìn thấy event fire)

### a) `LoggingDomainEventHandler<TEvent>` — open generic, catch-all

File: `Common/Persistence/LoggingDomainEventHandler.cs`

```csharp
public sealed class LoggingDomainEventHandler<TEvent> : INotificationHandler<TEvent>
    where TEvent : IDomainEvent
{
    public Task Handle(TEvent notification, CancellationToken ct)
    {
        _logger.LogInformation(
            "[DomainEvent] {EventType} (Id={EventId}, At={OccurredOnUtc:O})",
            typeof(TEvent).Name, notification.EventId, notification.OccurredOnUtc);
        return Task.CompletedTask;
    }
}
```

Đăng ký dạng open generic trong `AddDomainEventDispatching()`:

```csharp
services.AddTransient(typeof(INotificationHandler<>), typeof(LoggingDomainEventHandler<>));
```

⇒ Bất kỳ domain event mới nào bạn raise — không cần đăng ký gì thêm — đều có
1 dòng log `[DomainEvent] <EventType> ...` trong console.

### b) `UserRegisteredDomainEventHandler` — specific

File: `AuthService.Application/EventHandlers/UserRegisteredDomainEventHandler.cs`

```csharp
public sealed class UserRegisteredDomainEventHandler
    : INotificationHandler<UserRegisteredDomainEvent>
{
    public Task Handle(UserRegisteredDomainEvent notification, CancellationToken ct)
    {
        _logger.LogInformation(
            "[Welcome flow] User {UserId} ({Email} / {FullName}) just registered — kicking off onboarding",
            notification.UserId, notification.Email, notification.FullName);
        return Task.CompletedTask;
    }
}
```

Cùng file còn có `UserLoggedInDomainEventHandler` cho `UserLoggedInDomainEvent`.

Cả 2 specific handler được MediatR auto-discovery (do `RegisterServicesFromAssembly`).

## 6. Cách kiểm chứng

```bash
# Khởi động hạ tầng (nếu chưa có)
docker compose up -d sqlserver rabbitmq

# Chạy AuthService
dotnet run --project src/Services/AuthService/AuthService.API
```

Trong console của AuthService, sau khi gọi:

```bash
curl -X POST http://localhost:5101/auth/register \
  -H 'content-type: application/json' \
  -d '{"email":"alice@hdos.io","fullName":"Alice","password":"secret123"}'
```

Bạn sẽ thấy **tối thiểu 2 dòng log** xuất hiện ngay sau khi DB commit:

```
info: ... PublishDomainEventsInterceptor[0]   Dispatching 1 domain event(s) after SaveChanges
info: ... LoggingDomainEventHandler[0]        [DomainEvent] UserRegisteredDomainEvent (Id=..., At=...)
info: ... UserRegisteredDomainEventHandler[0] [Welcome flow] User ... just registered ...
```

(Dòng đầu `Dispatching ... domain event(s)` ở `Debug` level — bật
`"Hdos.Common.Persistence": "Debug"` trong `Logging:LogLevel` nếu muốn thấy.)

Tương tự với `POST /auth/login` — sẽ thấy `UserLoggedInDomainEvent` được fire
+ handler `[Audit]` chạy.

## 7. Thêm 1 domain event mới

Ví dụ: thêm `OrderCancelledDomainEvent` cho `OrderService`.

1. **Domain** — declare event:
   ```csharp
   // OrderService.Domain/Events/OrderCancelledDomainEvent.cs
   public sealed record OrderCancelledDomainEvent(Guid OrderId, Guid CustomerId) : DomainEvent;
   ```

2. **Aggregate** — raise:
   ```csharp
   public void Cancel()
   {
       if (Status == OrderStatus.Cancelled) return;
       Status = OrderStatus.Cancelled;
       UpdatedAtUtc = DateTime.UtcNow;
       RaiseDomainEvent(new OrderCancelledDomainEvent(Id, CustomerId));
   }
   ```

3. **(Optional) Specific handler** — chỉ thêm khi cần làm gì cụ thể:
   ```csharp
   // OrderService.Application/EventHandlers/OrderCancelledDomainEventHandler.cs
   public sealed class OrderCancelledDomainEventHandler
       : INotificationHandler<OrderCancelledDomainEvent>
   {
       public Task Handle(OrderCancelledDomainEvent n, CancellationToken ct)
       {
           // Vd: publish integration event để Notification gửi mail "đơn đã huỷ"
           return Task.CompletedTask;
       }
   }
   ```

Không cần đăng ký gì thêm. `LoggingDomainEventHandler<OrderCancelledDomainEvent>`
sẽ tự log nó. MediatR tự pick handler specific.

## 8. Caveat & best practice

| Vấn đề                                            | Hệ quả / cách xử lý                                                                                |
|---------------------------------------------------|----------------------------------------------------------------------------------------------------|
| Handler throw                                      | Vì post-save: DB **đã commit**, exception bubble lên ⇒ request trả 500 nhưng dữ liệu vẫn ở DB. Idempotency là việc của handler. |
| Handler chậm                                       | Nó chạy đồng bộ trong cùng request. Nếu cần I/O nặng → đẩy qua Integration Event để service khác xử lý async. |
| Handler resolve `DbContext`                        | OK — chung scope với DbContext gốc. Nhưng tránh `SaveChanges` lần 2 trong handler (event mới sẽ raise và dispatch tiếp — double-event nếu không cẩn thận). |
| Aggregate có nhiều event nhưng chỉ 1 publish       | Snapshot rồi clear là đúng. Nếu cần "atomic batch" thay vì từng cái, sửa interceptor để publish một event "bundle". |
| Test                                               | Có thể `new AuthDbContext(options, NullPublisher.Instance)` (cần thêm constructor overload) hoặc inject mock `IPublisher`. |

## 9. Liên hệ với Integration Event

Hiện tại `RegisterUserCommandHandler` vẫn publish integration event **bằng tay**
ngay sau `SaveChangesAsync`:

```csharp
await _uow.SaveChangesAsync(ct);
await _eventBus.PublishAsync(new UserRegisteredIntegrationEvent(...), ct);  // ← thủ công
```

Có dispatcher rồi, có thể refactor để domain event handler tự lo việc publish:

```csharp
// AuthService.Application/EventHandlers/UserRegisteredDomainEventHandler.cs
public sealed class UserRegisteredDomainEventHandler : INotificationHandler<UserRegisteredDomainEvent>
{
    private readonly IEventBus _bus;
    public UserRegisteredDomainEventHandler(IEventBus bus) => _bus = bus;

    public Task Handle(UserRegisteredDomainEvent n, CancellationToken ct) =>
        _bus.PublishAsync(new UserRegisteredIntegrationEvent(n.UserId, n.Email, n.FullName), ct);
}
```

Sau đó **xoá dòng `_eventBus.PublishAsync(...)`** khỏi `RegisterUserCommandHandler`.

Lợi ích:

- Use case handler chỉ làm 1 việc (ghi DB), không kiêm việc "thông báo".
- Mỗi domain event mới ⇒ chỉ cần 1 handler là tự được thông báo ra Rabbit, không lan vào nhiều use case.

Hạn chế (vẫn còn):

- Vẫn dual-write DB ↔ Rabbit. Crash giữa SaveChanges và Rabbit publish trong
  handler ⇒ vẫn mất event. Outbox mới giải quyết triệt để.

Mình **chưa làm refactor này** trong codebase để giữ thay đổi tối thiểu — bạn
quyết định khi nào nên áp dụng.
