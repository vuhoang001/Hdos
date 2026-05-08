# 04 — Feature: AuthService

`AuthService` quản lý user. Có 3 use case REST + 2 RPC gRPC + publish 2 event.

## 1. Endpoints

| Method | Path                | Use case             | Tầng dispatch                              |
|--------|---------------------|----------------------|--------------------------------------------|
| POST   | `/auth/register`    | Đăng ký user         | `RegisterUserCommand`                      |
| POST   | `/auth/login`       | Đăng nhập             | `LoginUserCommand`                         |
| GET    | `/auth/users/{id}`  | Lấy user theo id     | `GetUserByIdQuery`                         |
| GET    | `/auth/health`      | Health check          | (controller trực tiếp, không qua MediatR)  |
| gRPC   | `UserService.GetUserById` | Lookup user (cho service khác) | `UserGrpcService` → `IUserRepository` |
| gRPC   | `UserService.UserExists`  | Check tồn tại                | `UserGrpcService` → `IUserRepository` |

## 2. Domain (`AuthService.Domain`)

### `User : AggregateRoot<Guid>`

File: `src/Services/AuthService/AuthService.Domain/Entities/User.cs`

```csharp
public static User Register(Email email, string fullName, string passwordHash)
{
    var user = new User
    {
        Id = Guid.NewGuid(),
        Email = email,
        FullName = fullName.Trim(),
        PasswordHash = passwordHash
    };
    user.RaiseDomainEvent(new UserRegisteredDomainEvent(user.Id, user.Email.Value, user.FullName));
    return user;
}

public void RecordLogin()
{
    LastLoginUtc = DateTime.UtcNow;
    UpdatedAtUtc = DateTime.UtcNow;
    RaiseDomainEvent(new UserLoggedInDomainEvent(Id, Email.Value));
}
```

Quan sát:

- Constructor private + factory `Register(...)` ⇒ không thể tạo `User` invalid từ ngoài.
- Domain event được raise *trong* aggregate, không phải trong Application —
  giữ business rule tập trung.
- `Email` là Value Object, validate format trong `Email.Create(...)`.

### Repository interface

```csharp
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<User?> GetByEmailAsync(Email email, CancellationToken ct);
    Task<bool> ExistsByEmailAsync(Email email, CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
    void Update(User user);
}
public interface IUnitOfWork { Task<int> SaveChangesAsync(CancellationToken ct); }
```

Interface ở Domain, implementation ở Infrastructure (`UserRepository : IUserRepository`).

## 3. Use case `Register`

File: `Application/Features/Register/RegisterUserCommand.cs`

```
[Client]
   POST /auth/register {email, fullName, password}
        │
        ▼
[AuthController.Register]    (API)
        │ ISender.Send(cmd)
        ▼
[MediatR pipeline]
   ├── LoggingBehavior        "Handling RegisterUserCommand"
   └── ValidationBehavior     FluentValidation, throw ValidationException nếu fail
        │
        ▼
[RegisterUserCommandHandler] (Application)
   ├── Email.Create(...)              → Result<Email>
   ├── _users.ExistsByEmailAsync(...) → throw ConflictException nếu trùng
   ├── User.Register(...)             → raise UserRegisteredDomainEvent
   ├── _users.AddAsync(...)
   ├── _uow.SaveChangesAsync(...)     → EF Core commit
   └── _eventBus.PublishAsync(new UserRegisteredIntegrationEvent(...))
        │                              → RabbitMQ topic exchange hdos.events
        ▼
[Controller]
   ApiResponse<UserDto>.Ok(...) → 200 JSON
```

Lưu ý quan trọng:

- Domain event và Integration event là **hai thứ khác nhau**:
  - Domain event = trong process, để aggregate khác / handler trong cùng service biết.
  - Integration event = giữa các service, đi qua RabbitMQ.
- Domain event được dispatch tự động sau `SaveChangesAsync` qua
  `PublishDomainEventsInterceptor` (EF Core interceptor) → MediatR `IPublisher`.
  Hiện đã có `LoggingDomainEventHandler<TEvent>` (catch-all) +
  `UserRegisteredDomainEventHandler` / `UserLoggedInDomainEventHandler` (specific).
  Xem chi tiết: [11 — Domain Event Dispatcher](./11-domain-events.md).

## 4. Use case `Login`

File: `Application/Features/Login/LoginUserCommand.cs`

```csharp
public async Task<Result<LoginResultDto>> Handle(LoginUserCommand request, CancellationToken ct)
{
    var emailResult = Email.Create(request.Email);
    if (emailResult.IsFailure) return Result.Failure<LoginResultDto>(emailResult.Error);

    var user = await _users.GetByEmailAsync(emailResult.Value, ct);
    if (user is null || !_hasher.Verify(request.Password, user.PasswordHash))
        return Result.Failure<LoginResultDto>(Error.Unauthorized("Invalid credentials"));

    user.RecordLogin();                    // raise UserLoggedInDomainEvent + cập nhật LastLoginUtc
    _users.Update(user);
    await _uow.SaveChangesAsync(ct);

    await _eventBus.PublishAsync(
        new UserLoggedInIntegrationEvent(user.Id, user.Email.Value, DateTime.UtcNow), ct);

    var token = $"demo-token::{user.Id}::{Guid.NewGuid():N}"; // demo, không dùng prod
    return new LoginResultDto(user.Id, user.Email.Value, token);
}
```

- Failure được trả qua `Result<T>` thay vì throw — vì sai password là kết quả
  *kỳ vọng* trong flow login, không phải lỗi hệ thống.
- Token hiện chỉ là string demo. Xem mục "Next steps" trong README.md gốc để
  thêm JWT thực sự.

## 5. Use case `GetUserById`

File: `Application/Features/GetUser/GetUserByIdQuery.cs`

```csharp
public sealed record GetUserByIdQuery(Guid UserId) : IRequest<Result<UserDto>>;

public async Task<Result<UserDto>> Handle(GetUserByIdQuery request, CancellationToken ct)
{
    var user = await _users.GetByIdAsync(request.UserId, ct);
    if (user is null) return Result.Failure<UserDto>(Error.NotFound("User"));
    return new UserDto(user.Id, user.Email.Value, user.FullName, user.CreatedAtUtc);
}
```

Đây cũng chính là use case mà `UserGrpcService` (gRPC server) dùng — nhưng gRPC
gọi thẳng `IUserRepository` chứ không qua MediatR (xem [07 — gRPC](./07-grpc.md))
để tránh overhead pipeline cho call read-only nhỏ. Nếu cần áp dụng cùng
business rule, có thể đổi `UserGrpcService` để inject `ISender` và gọi
`Send(new GetUserByIdQuery(id))`.

## 6. Infrastructure (`AuthService.Infrastructure`)

| File                                                 | Vai trò                                                               |
|------------------------------------------------------|-----------------------------------------------------------------------|
| `Persistence/AuthDbContext.cs`                       | EF DbContext, override `SaveChangesAsync` để cập nhật audit timestamp.|
| `Persistence/Configurations/UserConfiguration.cs`    | Fluent API map `User` ↔ table `Users`.                                 |
| `Persistence/UserRepository.cs`                      | Implementation `IUserRepository` + `IUnitOfWork`.                      |
| `Security/PasswordHasher.cs`                         | BCrypt-style hash + verify (implement `IPasswordHasher`).              |
| `DependencyInjection.cs`                             | `AddAuthInfrastructure(IConfiguration)`: DbContext + repo + RabbitMQ.   |

`AddAuthInfrastructure` tự động gọi `services.AddRabbitMq(configuration)` từ
Common — Auth là publisher nên chỉ cần `IEventBus`, không cần consumer.

## 7. API (`AuthService.API`)

`Program.cs` (xem `src/Services/AuthService/AuthService.API/Program.cs`):

```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    var restPort = builder.Configuration.GetValue<int>("Kestrel:RestPort", 8080);
    var grpcPort = builder.Configuration.GetValue<int>("Kestrel:GrpcPort", 8081);
    options.ListenAnyIP(restPort, lo => lo.Protocols = HttpProtocols.Http1AndHttp2);
    options.ListenAnyIP(grpcPort, lo => lo.Protocols = HttpProtocols.Http2);
});

builder.Services.AddControllers();
builder.Services.AddGrpc();
builder.Services.AddAuthApplication();
builder.Services.AddAuthInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseHdosMiddleware();
app.MapControllers();
app.MapGrpcService<UserGrpcService>();
```

Hai cổng tách biệt:

- **REST + Swagger** → cổng `Kestrel:RestPort` (HTTP/1.1+HTTP/2). Default 8080
  trong container, 5101 trong dev local.
- **gRPC** → cổng `Kestrel:GrpcPort` (HTTP/2 only). Default 8081 / 5111.

Lý do tách: Swashbuckle/Swagger UI muốn HTTP/1.1, gRPC muốn HTTP/2 over
plaintext (h2c). Cùng một cổng vẫn được nhưng chạm vài cấu hình ALPN khó chịu.
Tách 2 cổng đơn giản hơn nhiều.

## 8. Cấu hình

`appsettings.json`:

```json
{
  "ConnectionStrings": {
    "AuthDb": "Server=localhost,1433;Database=AuthDb;..."
  },
  "RabbitMq": {
    "Host": "localhost", "Port": 5672, "Exchange": "hdos.events", ...
  }
}
```

`appsettings.Development.json` thêm `Kestrel:RestPort=5101`, `Kestrel:GrpcPort=5111`.

Container override qua env var:

```yaml
environment:
  Kestrel__RestPort: 8080
  Kestrel__GrpcPort: 8081
  ConnectionStrings__AuthDb: "Server=sqlserver,1433;Database=AuthDb;..."
  RabbitMq__Host: rabbitmq
```
