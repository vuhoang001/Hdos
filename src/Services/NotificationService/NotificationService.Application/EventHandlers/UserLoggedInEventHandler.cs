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

public sealed class UserRegisteredEventHandler : IIntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    private readonly INotificationRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly INotificationPusher _pusher;
    private readonly ILogger<UserRegisteredEventHandler> _logger;

    public UserRegisteredEventHandler(
        INotificationRepository repo,
        IUnitOfWork uow,
        INotificationPusher pusher,
        ILogger<UserRegisteredEventHandler> logger)
    {
        _repo = repo;
        _uow = uow;
        _pusher = pusher;
        _logger = logger;
    }

    public async Task HandleAsync(UserRegisteredIntegrationEvent @event, CancellationToken ct)
    {
        _logger.LogInformation("Received UserRegistered for {Email}", @event.Email);

        var notification = Notification.Create(
            recipient: @event.Email,
            subject: "Welcome to Hdos!",
            body: $"Hello {@event.FullName}, your account is ready.");

        notification.MarkSent();
        await _repo.AddAsync(notification, ct);
        await _uow.SaveChangesAsync(ct);

        await _pusher.PushToUserAsync(@event.Email, notification.ToDto(), ct);
    }
}

public sealed class OrderCreatedEventHandler : IIntegrationEventHandler<OrderCreatedIntegrationEvent>
{
    private readonly INotificationRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly INotificationPusher _pusher;
    private readonly ILogger<OrderCreatedEventHandler> _logger;

    public OrderCreatedEventHandler(
        INotificationRepository repo,
        IUnitOfWork uow,
        INotificationPusher pusher,
        ILogger<OrderCreatedEventHandler> logger)
    {
        _repo = repo;
        _uow = uow;
        _pusher = pusher;
        _logger = logger;
    }

    public async Task HandleAsync(OrderCreatedIntegrationEvent @event, CancellationToken ct)
    {
        _logger.LogInformation("Received OrderCreated {OrderId} for {Email}",
            @event.OrderId, @event.CustomerEmail);

        var lines = string.Join("\n",
            @event.Items.Select(i => $" - {i.ProductName} x{i.Quantity} @ {i.UnitPrice}"));

        var notification = Notification.Create(
            recipient: @event.CustomerEmail,
            subject: $"Order {@event.OrderId:N} confirmed",
            body: $"Thanks for your order!\nTotal: {@event.TotalAmount}\nItems:\n{lines}");

        notification.MarkSent();
        await _repo.AddAsync(notification, ct);
        await _uow.SaveChangesAsync(ct);

        await _pusher.PushToUserAsync(@event.CustomerEmail, notification.ToDto(), ct);
    }
}
