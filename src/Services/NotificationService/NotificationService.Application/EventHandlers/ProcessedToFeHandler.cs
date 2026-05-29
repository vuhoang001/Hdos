using Hdos.NotificationService.Application.DTOs;
using Hdos.NotificationService.Application.Realtime;
using Microsoft.Extensions.Logging;

namespace Hdos.NotificationService.Application.EventHandlers;

public sealed class ProcessedToFeHandler(
    INotificationPusher pusher,
    ILogger<ProcessedToFeHandler> logger)
{
    public async Task HandleAsync(ProcessedToFeMessage message, CancellationToken ct)
    {
        logger.LogInformation(
            "Received processed_to_fe: eventType={EventType}", message.EventType);

        await pusher.BroadcastEventAsync(
            "processed-to-fe",
            new { eventType = message.EventType, payload = message.Payload, data = message.Data },
            ct);
    }
}
