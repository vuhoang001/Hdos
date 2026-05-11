# 12 — Kiểm thử

---

## Triết lý kiểm thử

Dự án chỉ có **unit tests** — không có integration tests hay end-to-end tests. Lý do:

- Unit tests chạy không cần database, RabbitMQ, hay bất kỳ infrastructure nào → nhanh, chạy được trên CI không cần setup phức tạp
- Business logic (domain + application) là phần quan trọng nhất và có thể test độc lập
- Infrastructure code (EF Core, RabbitMQ client) đơn giản, ít logic → lợi ích của integration test không đủ bù chi phí setup

---

## Stack

| Thư viện | Vai trò |
|---------|---------|
| **xUnit** | Test framework — runner + assertions cơ bản |
| **FluentAssertions** | Assertion DSL mạnh hơn (`result.IsSuccess.Should().BeTrue()`) |
| **NSubstitute** | Mocking library — tạo mock cho interfaces |
| **coverlet** | Code coverage — tự động collect khi chạy `dotnet test` |

---

## Cấu trúc test projects

```
tests/
├── Hdos.BuildingBlocks.Tests/    ← Test Result, ValueObject, AggregateRoot
├── Hdos.AuthService.Tests/       ← Test AuthService domain + application
├── Hdos.OrderService.Tests/      ← Test OrderService domain + application
└── Hdos.NotificationService.Tests/
```

Mỗi project test là mirror của service tương ứng:
```
tests/Hdos.AuthService.Tests/
├── Domain/
│   ├── UserTests.cs
│   └── EmailTests.cs
├── Application/
│   ├── LoginUserCommandHandlerTests.cs
│   ├── RegisterUserCommandHandlerTests.cs
│   └── GetUserByIdQueryHandlerTests.cs
├── Grpc/
│   └── UserGrpcServiceTests.cs
└── Validators/
    └── ValidatorTests.cs
```

---

## Chạy test

```bash
# Tất cả
dotnet test

# Một project cụ thể
dotnet test tests/Hdos.AuthService.Tests/

# Với output verbose
dotnet test --logger "console;verbosity=detailed"

# Với coverage (cần coverlet.collector đã cài)
dotnet test --collect:"XPlat Code Coverage"
# Kết quả: tests/<project>/TestResults/<guid>/coverage.cobertura.xml

# Chạy test theo tên
dotnet test --filter "FullyQualifiedName~LoginUserCommandHandlerTests"
```

---

## Ví dụ test Domain

Domain objects là pure C# — không cần mock gì, test trực tiếp:

```csharp
// tests/Hdos.OrderService.Tests/Domain/OrderTests.cs

[Fact]
public void Create_LowercasesAndTrimsEmail_AndComputesTotal()
{
    var order = Order.Create(
        customerId: Guid.NewGuid(),
        customerEmail: "  ALICE@hdos.io  ",
        items: new[] { ("Book", 2, 15.50m, "USD"), ("Pen", 3, 2m, "USD") });

    order.CustomerEmail.Should().Be("alice@hdos.io");  // trim + lowercase
    order.Total.Amount.Should().Be(2 * 15.50m + 3 * 2m);
    order.Items.Should().HaveCount(2);
}

[Fact]
public void Create_RaisesOrderCreatedDomainEvent()
{
    var order = Order.Create(Guid.NewGuid(), "a@b.io", new[] { ("Book", 1, 10m, "USD") });

    order.DomainEvents.Should().ContainSingle()
        .Which.Should().BeOfType<OrderCreatedDomainEvent>()
        .Which.OrderId.Should().Be(order.Id);
}

[Fact]
public void Confirm_NonPending_Throws()
{
    var order = Order.Create(Guid.NewGuid(), "a@b.io", new[] { ("X", 1, 1m, "USD") });
    order.Cancel();

    var act = () => order.Confirm();
    act.Should().Throw<InvalidOperationException>();
}
```

---

## Ví dụ test Application (Handler)

Application handlers phụ thuộc vào repositories và services qua interface. Dùng NSubstitute để mock:

```csharp
// tests/Hdos.AuthService.Tests/Application/LoginUserCommandHandlerTests.cs

public sealed class LoginUserCommandHandlerTests
{
    // Tạo mock cho tất cả dependencies
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IEventBus _bus = Substitute.For<IEventBus>();
    private readonly IJwtTokenIssuer _tokenIssuer = Substitute.For<IJwtTokenIssuer>();

    public LoginUserCommandHandlerTests()
    {
        // Setup default behavior
        _tokenIssuer.Issue(Arg.Any<Guid>(), Arg.Any<string>())
            .Returns(new JwtTokenResult("test-token", DateTime.UtcNow.AddHours(1)));
    }

    [Fact]
    public async Task Handle_ValidCredentials_RecordsLoginAndPublishes()
    {
        var user = User.Register(Email.Create("alice@hdos.io").Value, "Alice", "stored-hash");
        _users.GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("secret", user.PasswordHash).Returns(true);

        var handler = new LoginUserCommandHandler(_users, _uow, _hasher, _bus, _tokenIssuer);
        var result = await handler.Handle(new LoginUserCommand("alice@hdos.io", "secret"), CancellationToken.None);

        // Assert kết quả
        result.IsSuccess.Should().BeTrue();
        result.Value.Token.Should().NotBeNullOrWhiteSpace();

        // Assert side effects
        user.LastLoginUtc.Should().NotBeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // Assert event published đúng
        await _bus.Received(1).PublishAsync(
            Arg.Is<UserLoggedInIntegrationEvent>(e => e.UserId == user.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WrongPassword_ReturnsUnauthorized_AndDoesNotPublish()
    {
        var user = User.Register(Email.Create("alice@hdos.io").Value, "Alice", "stored-hash");
        _users.GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var result = await new LoginUserCommandHandler(_users, _uow, _hasher, _bus, _tokenIssuer)
            .Handle(new LoginUserCommand("alice@hdos.io", "wrong"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Unauthorized");

        // Verify không publish event khi fail
        await _bus.DidNotReceive().PublishAsync(
            Arg.Any<UserLoggedInIntegrationEvent>(), Arg.Any<CancellationToken>());
    }
}
```

**Pattern chung:**
1. Setup mock responses với `.Returns(...)`
2. Gọi handler
3. Assert `result.IsSuccess` / `result.IsFailure` + `result.Error.Code`
4. Assert side effects với `Received(n)` / `DidNotReceive()`

---

## Ví dụ test Result (BuildingBlocks)

```csharp
// tests/Hdos.BuildingBlocks.Tests/SharedKernel/ResultTests.cs

[Fact]
public void Failure_Generic_ThrowsOnValueAccess()
{
    var result = Result.Failure<int>(Error.NotFound("Thing"));

    var act = () => _ = result.Value;
    act.Should().Throw<InvalidOperationException>();
}

[Theory]
[InlineData("NotFound")]
[InlineData("Validation")]
[InlineData("Conflict")]
[InlineData("Unauthorized")]
public void ErrorFactories_ProduceCanonicalCodes(string expectedCode)
{
    Error error = expectedCode switch
    {
        "NotFound"     => Error.NotFound("X"),
        "Validation"   => Error.Validation("x"),
        "Conflict"     => Error.Conflict("x"),
        "Unauthorized" => Error.Unauthorized(),
        _              => throw new InvalidOperationException()
    };

    error.Code.Should().Be(expectedCode);
    error.Message.Should().NotBeNullOrEmpty();
}
```

`[Theory]` + `[InlineData]` — chạy cùng logic với nhiều inputs. Tốt cho test value objects, error codes, và business rules có nhiều case.

---

## Một số NSubstitute patterns thường dùng

```csharp
// Mock trả giá trị
_repo.GetByIdAsync(id, ct).Returns(entity);

// Mock trả null
_repo.GetByEmailAsync(email, ct).Returns((User?)null);

// Mock ném exception
_repo.AddAsync(Arg.Any<User>(), ct).Throws(new Exception("DB error"));

// Verify đã gọi với argument cụ thể
await _bus.Received(1).PublishAsync(
    Arg.Is<UserRegisteredIntegrationEvent>(e => e.Email == "alice@hdos.io"),
    Arg.Any<CancellationToken>());

// Verify không gọi
await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());

// Verify số lần gọi
_hasher.Received(2).Verify(Arg.Any<string>(), Arg.Any<string>());
```

---

## Coverage trong CI

CI upload `.trx` file (test results) như artifact:

```yaml
- uses: actions/upload-artifact@v4
  if: always()   # Upload ngay cả khi test fail
  with:
    name: test-results
    path: "**/*.trx"
```

Xem kết quả: **GitHub → Actions → workflow run → Artifacts → test-results**.

---

## Thêm test cho code mới

Khi thêm một handler mới (ví dụ `ResetPasswordCommandHandler`):

1. Tạo file trong `tests/Hdos.AuthService.Tests/Application/ResetPasswordCommandHandlerTests.cs`
2. Follow pattern: mock tất cả dependencies với `Substitute.For<>`, test happy path + các failure cases
3. Đặt tên test theo convention: `<Method>_<Condition>_<Expected>` — ví dụ: `Handle_ExpiredToken_ReturnsUnauthorized`

Không cần thêm gì vào CI — `dotnet test` tự discover tất cả `[Fact]` và `[Theory]`.
