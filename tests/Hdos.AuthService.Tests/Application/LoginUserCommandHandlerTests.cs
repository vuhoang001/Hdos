using FluentAssertions;
using Hdos.AuthService.Application.Abstractions;
using Hdos.AuthService.Application.Features.Login;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Hdos.AuthService.Domain.ValueObjects;
using Hdos.Common.Auth;
using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using NSubstitute;
using Xunit;

namespace Hdos.AuthService.Tests.Application;

public sealed class LoginUserCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IEventBus _bus = Substitute.For<IEventBus>();
    private readonly IJwtTokenIssuer _tokenIssuer = Substitute.For<IJwtTokenIssuer>();

    public LoginUserCommandHandlerTests()
    {
        _tokenIssuer.Issue(Arg.Any<Guid>(), Arg.Any<string>())
            .Returns(_ => new JwtTokenResult("test-token", DateTime.UtcNow.AddHours(1)));
    }

    private LoginUserCommandHandler NewHandler() => new(_users, _uow, _hasher, _bus, _tokenIssuer);

    private static User AUser() => User.Register(Email.Create("alice@hdos.io").Value, "Alice", "stored-hash");

    [Fact]
    public async Task Handle_ValidCredentials_RecordsLoginAndPublishes()
    {
        var user = AUser();
        _users.GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("secret", user.PasswordHash).Returns(true);

        var result = await NewHandler().Handle(
            new LoginUserCommand("alice@hdos.io", "secret"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(user.Id);
        result.Value.Email.Should().Be("alice@hdos.io");
        result.Value.Token.Should().NotBeNullOrWhiteSpace();

        user.LastLoginUtc.Should().NotBeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<UserLoggedInIntegrationEvent>(e => e.UserId == user.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownEmail_ReturnsUnauthorized()
    {
        _users.GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await NewHandler().Handle(
            new LoginUserCommand("ghost@hdos.io", "secret"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Unauthorized");

        await _bus.DidNotReceive().PublishAsync(
            Arg.Any<UserLoggedInIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WrongPassword_ReturnsUnauthorized()
    {
        var user = AUser();
        _users.GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var result = await NewHandler().Handle(
            new LoginUserCommand("alice@hdos.io", "wrong"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Unauthorized");

        await _bus.DidNotReceive().PublishAsync(
            Arg.Any<UserLoggedInIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidEmailFormat_ReturnsValidation()
    {
        var result = await NewHandler().Handle(
            new LoginUserCommand("not-an-email", "secret"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
    }
}
