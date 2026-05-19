using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.DTOs;
using Hdos.NotificationService.Application.Realtime;
using Hdos.NotificationService.Domain.Entities;
using Hdos.NotificationService.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Hdos.NotificationService.Application.EventHandlers;

public sealed class OrderConfirmedEventHandler(
    INotificationRepository repo,
    IUnitOfWork uow,
    INotificationPusher pusher,
    ILogger<OrderConfirmedEventHandler> logger)
    : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    public async Task HandleAsync(OrderConfirmedIntegrationEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Received OrderConfirmed {OrderId} for {Email}", @event.OrderId, @event.CustomerEmail);

        var notification = Notification.Create(
            recipient: @event.CustomerEmail,
            subject: $"Đơn hàng {@event.OrderId:N} đã được xác nhận",
            body: $"Đơn hàng của bạn đã được xác nhận thành công. Trạng thái: {@event.OrderStatus}.");

        notification.MarkSent();
        await repo.AddAsync(notification, ct);
        await uow.SaveChangesAsync(ct);

        await pusher.PushToUserAsync(@event.CustomerEmail, notification.ToDto(), ct);
    }
}