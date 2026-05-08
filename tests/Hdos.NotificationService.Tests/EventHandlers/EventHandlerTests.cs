using FluentAssertions;
using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.EventHandlers;
using Hdos.NotificationService.Domain.Entities;
using Hdos.NotificationService.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Hdos.NotificationService.Tests.EventHandlers;

public sealed class UserLoggedInEventHandlerTests
{
    [Fact]
    public async Task Handle_PersistsSentNotification()
    {
        var repo = Substitute.For<INotificationRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var handler = new UserLoggedInEventHandler(repo, uow,
            NullLogger<UserLoggedInEventHandler>.Instance);

        var captured = (Notification?)null;
        await repo.AddAsync(Arg.Do<Notification>(n => captured = n), Arg.Any<CancellationToken>());

        await handler.HandleAsync(
            new UserLoggedInIntegrationEvent(Guid.NewGuid(), "alice@hdos.io", DateTime.UtcNow),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Recipient.Should().Be("alice@hdos.io");
        captured.Status.Should().Be(NotificationStatus.Sent);
        captured.Subject.Should().Contain("login");
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

public sealed class UserRegisteredEventHandlerTests
{
    [Fact]
    public async Task Handle_BuildsWelcomeNotification()
    {
        var repo = Substitute.For<INotificationRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var handler = new UserRegisteredEventHandler(repo, uow,
            NullLogger<UserRegisteredEventHandler>.Instance);

        var captured = (Notification?)null;
        await repo.AddAsync(Arg.Do<Notification>(n => captured = n), Arg.Any<CancellationToken>());

        await handler.HandleAsync(
            new UserRegisteredIntegrationEvent(Guid.NewGuid(), "alice@hdos.io", "Alice"),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Recipient.Should().Be("alice@hdos.io");
        captured.Body.Should().Contain("Alice");
        captured.Status.Should().Be(NotificationStatus.Sent);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

public sealed class OrderCreatedEventHandlerTests
{
    [Fact]
    public async Task Handle_BuildsOrderConfirmation_WithLinesAndTotal()
    {
        var repo = Substitute.For<INotificationRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var handler = new OrderCreatedEventHandler(repo, uow,
            NullLogger<OrderCreatedEventHandler>.Instance);

        var captured = (Notification?)null;
        await repo.AddAsync(Arg.Do<Notification>(n => captured = n), Arg.Any<CancellationToken>());

        var ev = new OrderCreatedIntegrationEvent(
            Guid.NewGuid(), Guid.NewGuid(), "alice@hdos.io",
            TotalAmount: 31.00m,
            Items: new[]
            {
                new OrderItemDto("Book", 2, 15.50m),
                new OrderItemDto("Pen", 0, 0m) // edge: zero qty in line text
            });

        await handler.HandleAsync(ev, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Recipient.Should().Be("alice@hdos.io");
        captured.Subject.Should().Contain("confirmed");
        captured.Body.Should().Contain("Book");
        captured.Body.Should().Contain("31.00");
        captured.Status.Should().Be(NotificationStatus.Sent);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
