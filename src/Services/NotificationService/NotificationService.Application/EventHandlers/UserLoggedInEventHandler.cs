using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.DTOs;
using Hdos.NotificationService.Application.Realtime;
using Hdos.NotificationService.Domain.Entities;
using Hdos.NotificationService.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Hdos.NotificationService.Application.EventHandlers;

public sealed class UserLoggedInEventHandler(
    INotificationRepository repo,
    IUnitOfWork uow,
    INotificationPusher pusher,
    ILogger<UserLoggedInEventHandler> logger)
    : IIntegrationEventHandler<UserLoggedInIntegrationEvent>
{
    public async Task HandleAsync(UserLoggedInIntegrationEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Received UserLoggedIn for {Email}", @event.Email);

        var notification = Notification.Create(
            recipient: @event.Email,
            subject: "New login on your account",
            body: $"Hi! A new login was detected at {@event.LoggedInAtUtc:u}. If this wasn't you, please reset your password.");

        notification.MarkSent();
        await repo.AddAsync(notification, ct);
        await uow.SaveChangesAsync(ct);

        await pusher.PushToUserAsync(@event.Email, notification.ToDto(), ct);
    }
}

public sealed class UserRegisteredEventHandler(
    INotificationRepository repo,
    IUnitOfWork uow,
    INotificationPusher pusher,
    ILogger<UserRegisteredEventHandler> logger)
    : IIntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    public async Task HandleAsync(UserRegisteredIntegrationEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Received UserRegistered for {Email}", @event.Email);

        var notification = Notification.Create(
            recipient: @event.Email,
            subject: "Welcome to Hdos!",
            body: $"Hello {@event.FullName}, your account is ready.");

        notification.MarkSent();
        await repo.AddAsync(notification, ct);
        await uow.SaveChangesAsync(ct);

        await pusher.PushToUserAsync(@event.Email, notification.ToDto(), ct);
    }
}

