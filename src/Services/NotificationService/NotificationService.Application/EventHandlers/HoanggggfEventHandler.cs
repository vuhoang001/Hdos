using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.DTOs;
using Hdos.NotificationService.Application.Realtime;

namespace Hdos.NotificationService.Application.EventHandlers;

public sealed class HoanggggfEventHandler(INotificationPusher pusher)
    : IIntegrationEventHandler<HoanggggfIntegrationEvent>
{
    public async Task HandleAsync(HoanggggfIntegrationEvent @event, CancellationToken ct)
    {
        var notification = new NotificationDto(
            Id: Guid.NewGuid(),
            Recipient: "all",
            Subject: $"Xin chào {@event.Name}!",
            Body: $"Sự kiện Hoanggggf: Name={@event.Name}, Age={@event.Age}",
            Channel: "SSE",
            Status: "Sent",
            CreatedAtUtc: DateTime.UtcNow,
            SentAtUtc: DateTime.UtcNow);

        await pusher.BroadcastAsync(notification, ct);
    }
}