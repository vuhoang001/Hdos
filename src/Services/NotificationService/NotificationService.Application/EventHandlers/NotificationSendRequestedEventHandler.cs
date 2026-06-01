using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.DTOs;
using Hdos.NotificationService.Application.Realtime;
using Hdos.NotificationService.Domain.Entities;
using Hdos.NotificationService.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Hdos.NotificationService.Application.EventHandlers;

public sealed class NotificationSendRequestedEventHandler(
    INotificationRepository repo,
    IUnitOfWork uow,
    INotificationPusher pusher,
    ILogger<NotificationSendRequestedEventHandler> logger)
    : IIntegrationEventHandler<NotificationSendRequestedIntegrationEvent>
{
    public async Task HandleAsync(NotificationSendRequestedIntegrationEvent @event, CancellationToken ct)
    {
        logger.LogInformation(
            "Processing async notification. CorrelationId={CorrelationId} Recipient={Recipient}",
            @event.RequestId, @event.RecipientEmail);

        var notification = Notification.Create(
            recipient: @event.RecipientEmail,
            subject: @event.Subject,
            body: @event.Body);

        notification.MarkSent();
        await repo.AddAsync(notification, ct);
        await uow.SaveChangesAsync(ct);

        await pusher.PushToUserAsync(@event.RecipientEmail, notification.ToDto(), ct);
    }
}
