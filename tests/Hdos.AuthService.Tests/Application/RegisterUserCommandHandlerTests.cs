using FluentAssertions;
using Hdos.AuthService.Application.Abstractions;
using Hdos.AuthService.Application.Features.Register;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Hdos.AuthService.Domain.ValueObjects;
using Hdos.Common.Exceptions;
using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using NSubstitute;
using Xunit;

namespace Hdos.AuthService.Tests.Application;

public sealed class RegisterUserCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IEventBus _bus = Substitute.For<IEventBus>();

    private RegisterUserCommandHandler NewHandler() => new(_users, _uow, _hasher, _bus);

    [Fact]
    public async Task Handle_ValidInput_PersistsAndPublishes()
    {
        _hasher.Hash("secret").Returns("hashed");
        _users.ExistsByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await NewHandler().Handle(
            new RegisterUserCommand("alice@hdos.io", "Alice", "secret"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be("alice@hdos.io");
        result.Value.FullName.Should().Be("Alice");

        await _users.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<UserRegisteredIntegrationEvent>(e =>
                e.Email == "alice@hdos.io" && e.FullName == "Alice"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidEmail_ReturnsFailure_AndDoesNotPersist()
    {
        var result = await NewHandler().Handle(
            new RegisterUserCommand("not-an-email", "Alice", "secret"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");

        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(
            Arg.Any<UserRegisteredIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateEmail_ThrowsConflict()
    {
        _users.ExistsByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(true);

        var act = async () => await NewHandler().Handle(
            new RegisterUserCommand("alice@hdos.io", "Alice", "secret"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();

        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(
            Arg.Any<UserRegisteredIntegrationEvent>(), Arg.Any<CancellationToken>());
    }
}
