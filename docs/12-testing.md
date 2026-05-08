# 12 — Testing

Testing layout, stack, và cách chạy.

## 1. Stack

| Thư viện              | Phiên bản | Dùng để                                                                  |
|-----------------------|-----------|--------------------------------------------------------------------------|
| `xunit`               | 2.9.x     | Test framework                                                            |
| `xunit.runner.visualstudio` | 2.8.x | Test discovery cho `dotnet test` / IDE                                |
| `FluentAssertions`    | 6.12.x    | Assertion API kiểu `.Should().Be(...)` — thông báo lỗi dễ đọc            |
| `NSubstitute`         | 5.1.x     | Mock interface/abstract class. Syntax gọn hơn Moq, không bị tranh cãi telemetry. |
| `Microsoft.EntityFrameworkCore.InMemory` | 8.0.x | In-memory DbContext cho integration test của interceptor       |
| `Microsoft.NET.Test.Sdk` | 17.11.x | Test runner host                                                       |
| `coverlet.collector`  | 6.0.x     | Coverage (mặc định khi `dotnet test --collect:"XPlat Code Coverage"`)     |

## 2. Cấu trúc

```
tests/
├── Hdos.BuildingBlocks.Tests/
│   ├── SharedKernel/         (Result, ValueObject, AggregateRoot)
│   ├── Contracts/            (IntegrationEvent base)
│   └── Persistence/          (PublishDomainEventsInterceptor, LoggingDomainEventHandler)
├── Hdos.AuthService.Tests/
│   ├── Domain/               (Email VO, User aggregate)
│   ├── Application/          (Register, Login, GetUser handlers)
│   ├── Validators/           (FluentValidation rules)
│   └── Grpc/                 (UserGrpcService — direct unit test, không cần channel)
├── Hdos.OrderService.Tests/
│   ├── Domain/               (Money VO, Order aggregate)
│   ├── Application/          (CreateOrder, GetOrder handlers — mock IUserLookupService)
│   └── Validators/
└── Hdos.NotificationService.Tests/
    ├── Domain/               (Notification aggregate)
    ├── Application/          (ListRecentNotifications query)
    └── EventHandlers/        (3 IIntegrationEventHandler — UserLoggedIn / UserRegistered / OrderCreated)
```

Mỗi service có 1 test project dedicated. Folder bên trong gương đúng layer
trong `src/`. Naming convention: `<TypeUnderTest>Tests.cs`.

## 3. Chiến lược test

### 3.1 Unit (đa số) — không I/O, không network

Test hết Domain + Application handler + Validator. Mock mọi cổng ngoài qua
`Substitute.For<T>()`:

| Service             | Mock                                                                                |
|---------------------|-------------------------------------------------------------------------------------|
| AuthService         | `IUserRepository`, `IUnitOfWork`, `IPasswordHasher`, `IEventBus`                    |
| OrderService        | `IOrderRepository`, `IUnitOfWork`, `IEventBus`, `IUserLookupService` (gRPC port)    |
| NotificationService | `INotificationRepository`, `IUnitOfWork`                                            |

Giá trị: chạy < 200 ms cho từng project → feedback loop nhanh khi sửa code.

### 3.2 Integration (chọn lọc) — EF Core InMemory

| Test                                                  | Lý do                                                                |
|-------------------------------------------------------|----------------------------------------------------------------------|
| `PublishDomainEventsInterceptorTests`                 | Verify EF interceptor thật sự chạy: aggregate raise event → SaveChanges → `IPublisher.Publish` được gọi đúng số lần / clear list / không re-dispatch sau update không có event mới. |

InMemory provider không hỗ trợ transaction/relational features đầy đủ, nhưng
**hỗ trợ interceptor** — đủ cho test này. Không cần Testcontainers / SQL thật.

### 3.3 gRPC server test — direct, không cần channel

`UserGrpcServiceTests` instantiate trực tiếp `UserGrpcService` với
`IUserRepository` mock + một `TestServerCallContext` stub (định nghĩa trong
chính file test). Không spin up Kestrel, không in-process channel.

Lý do: gRPC service cuối cùng cũng chỉ là một class C# — không cần test qua
network để verify business logic. Wire-up Kestrel là việc của framework, không
phải của business logic mình viết.

### 3.4 Những gì KHÔNG test (cố ý)

| Bị bỏ                              | Lý do                                                                         |
|------------------------------------|-------------------------------------------------------------------------------|
| `RabbitMqEventBus` publish thật    | Cần broker. Cần Testcontainer → ngoài scope unit test. Logic publish chỉ là wrapper rất mỏng quanh `BasicPublish`. |
| `RabbitMqConsumerHostedService`    | Tương tự — connection lifecycle, ack/nack thật chỉ test được với broker.      |
| EF `Repository` (DB ops)           | InMemory không phản ánh đúng quan hệ navigation. Muốn realistic phải SQLite/SQL → test riêng `IntegrationTests` project. |
| `AuthUserLookupClient` (gRPC client adapter) | Để mock `UserService.UserServiceClient.GetUserByIdAsync` cần `Grpc.Core.Testing`. Logic adapter chỉ là try/catch đơn giản, đã được cover gián tiếp qua `CreateOrderCommandHandlerTests`. |
| API endpoint qua HTTP              | Cần `WebApplicationFactory` + cấu hình thay thế DB/Rabbit. Đẩy sang E2E phase. |

Khi nào nên thêm: nếu sau này có incident xảy ra ở chính phần bị bỏ, đó là tín
hiệu cần thêm test cho nó. Đừng "test cho có" mà không có giá trị thực.

## 4. Cách chạy

### Toàn bộ

```bash
dotnet test Hdos.sln
```

Hiện tại: **110 test, 0 fail**.

```
Hdos.BuildingBlocks.Tests       : 26 passed
Hdos.AuthService.Tests          : 41 passed
Hdos.OrderService.Tests         : 27 passed
Hdos.NotificationService.Tests  : 16 passed
```

### Chỉ 1 project

```bash
dotnet test tests/Hdos.AuthService.Tests/Hdos.AuthService.Tests.csproj
```

### Chỉ 1 class hoặc 1 method

```bash
dotnet test --filter "FullyQualifiedName~RegisterUserCommandHandlerTests"
dotnet test --filter "FullyQualifiedName~CreateOrderCommandHandlerTests.Handle_UserVerified_PersistsAndPublishes"
```

### Coverage

```bash
dotnet test --collect:"XPlat Code Coverage"
# kết quả: tests/<project>/TestResults/<guid>/coverage.cobertura.xml
```

Convert sang HTML bằng `reportgenerator`:

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"tests/**/coverage.cobertura.xml" \
                -targetdir:"coverage-report" \
                -reporttypes:Html
```

## 5. Pattern thường dùng

### 5.1 Setup mock với NSubstitute

```csharp
private readonly IUserRepository _users = Substitute.For<IUserRepository>();

// stub return
_users.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(user);

// verify call
await _users.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
await _users.DidNotReceive().Update(Arg.Any<User>());

// capture argument
var captured = (Notification?)null;
await repo.AddAsync(Arg.Do<Notification>(n => captured = n), Arg.Any<CancellationToken>());
```

### 5.2 Validator test với FluentValidation

```csharp
using FluentValidation.TestHelper;

_v.TestValidate(cmd)
    .ShouldHaveValidationErrorFor(x => x.Email);

_v.TestValidate(cmd)
    .ShouldNotHaveAnyValidationErrors();
```

### 5.3 Test domain event được raise

```csharp
var user = User.Register(email, "Alice", "h");

user.DomainEvents.Should().ContainSingle()
    .Which.Should().BeOfType<UserRegisteredDomainEvent>()
    .Which.UserId.Should().Be(user.Id);
```

### 5.4 Interceptor test với InMemory DbContext

```csharp
var publisher = Substitute.For<IPublisher>();
var interceptor = new PublishDomainEventsInterceptor(publisher, NullLogger.Instance);

var options = new DbContextOptionsBuilder<MyDbContext>()
    .UseInMemoryDatabase($"db-{Guid.NewGuid()}")
    .AddInterceptors(interceptor)
    .Options;

using var db = new MyDbContext(options);
db.Add(MyAggregate.Create("..."));
await db.SaveChangesAsync();

await publisher.Received(1).Publish(Arg.Any<MyDomainEvent>(), Arg.Any<CancellationToken>());
```

## 6. Caveat & gotcha

| Vấn đề                                                                     | Cách xử                                                                |
|----------------------------------------------------------------------------|------------------------------------------------------------------------|
| `Castle DynamicProxy: type X is not accessible`                             | Generic param/class phải `public` hoặc `internal` (NSubstitute proxy ILogger<T> cần T accessible). |
| InMemory DB không enforce required/unique                                   | Chấp nhận: chỉ dùng InMemory để test interceptor + simple ChangeTracker, không test constraint. Dùng SQLite/Docker khi cần realism. |
| Test gRPC service không thoát (hang)                                         | Dùng `TestServerCallContext` (file `Grpc/UserGrpcServiceTests.cs`) thay vì instance thật từ `Grpc.Core.Server`. |
| Test handler MediatR cần pipeline behavior                                  | Test handler trực tiếp, bỏ qua pipeline. Behavior (Logging, Validation) test riêng nếu cần. |
| Random Guid sinh trong `User.Register()` làm equality check khó             | So sánh field cụ thể (`user.Id`, `user.Email.Value`) thay vì cả entity. |

## 7. Mở rộng

| Cần thêm                          | Hướng đi                                                                                         |
|-----------------------------------|--------------------------------------------------------------------------------------------------|
| Integration test thật với DB       | Tạo `tests/Hdos.<Service>.IntegrationTests/`, dùng `Testcontainers.MsSql` + `WebApplicationFactory`. |
| End-to-end qua API                 | `WebApplicationFactory<Program>` + override `appsettings` để dùng InMemory/SQLite + test container. |
| Test gRPC qua channel thật         | `Grpc.AspNetCore.Server.ClientFactory` + `TestServer`, hoặc `WebApplicationFactory.CreateClient()`. |
| Property-based testing             | `FsCheck` cho VO (Email/Money) — tự generate input ngẫu nhiên.                                    |
| Mutation testing                   | `Stryker.NET` — chạy trên CI để bắt test không thực sự kiểm chứng gì.                              |
