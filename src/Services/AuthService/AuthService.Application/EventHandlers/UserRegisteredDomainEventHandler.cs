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
public sealed class UserRegisteredDomainEventHandler : INotificationHandler<UserRegisteredDomainEvent>
{
    private readonly ILogger<UserRegisteredDomainEventHandler> _logger;

    public UserRegisteredDomainEventHandler(ILogger<UserRegisteredDomainEventHandler> logger)
        => _logger = logger;

    public Task Handle(UserRegisteredDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[Welcome flow] User {UserId} ({Email} / {FullName}) just registered — kicking off onboarding",
            notification.UserId, notification.Email, notification.FullName);
        return Task.CompletedTask;
    }
}

public sealed class UserLoggedInDomainEventHandler : INotificationHandler<UserLoggedInDomainEvent>
{
    private readonly ILogger<UserLoggedInDomainEventHandler> _logger;

    public UserLoggedInDomainEventHandler(ILogger<UserLoggedInDomainEventHandler> logger)
        => _logger = logger;

    public Task Handle(UserLoggedInDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[Audit] Login recorded for {UserId} ({Email})",
            notification.UserId, notification.Email);
        return Task.CompletedTask;
    }
}
