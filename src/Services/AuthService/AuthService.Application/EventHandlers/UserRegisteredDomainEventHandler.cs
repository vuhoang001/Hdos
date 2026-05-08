using Hdos.AuthService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Hdos.AuthService.Application.EventHandlers;

/// <summary>
/// Demo handler — proves that specific INotificationHandler&lt;TDomainEvent&gt;
/// implementations get picked up alongside the open-generic LoggingHandler.
/// MediatR auto-discovers it through AddMediatR(...RegisterServicesFromAssembly).
/// In real code this is where you'd send a welcome email, seed a user profile
/// in another bounded context, etc.
/// </summary>
public sealed class UserRegisteredDomainEventHandler(ILogger<UserRegisteredDomainEventHandler> logger)
    : INotificationHandler<UserRegisteredDomainEvent>
{
    public Task Handle(UserRegisteredDomainEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[Welcome flow] User {UserId} ({Email} / {FullName}) just registered — kicking off onboarding",
            notification.UserId, notification.Email, notification.FullName);
        return Task.CompletedTask;
    }
}

public sealed class UserLoggedInDomainEventHandler(ILogger<UserLoggedInDomainEventHandler> logger)
    : INotificationHandler<UserLoggedInDomainEvent>
{
    public Task Handle(UserLoggedInDomainEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[Audit] Login recorded for {UserId} ({Email})",
            notification.UserId, notification.Email);
        return Task.CompletedTask;
    }
}
