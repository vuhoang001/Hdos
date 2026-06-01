using Hdos.NotificationService.Application.DTOs;
using Hdos.NotificationService.Application.Realtime;
using Microsoft.Extensions.Logging;

namespace Hdos.NotificationService.Application.EventHandlers;

public sealed class DebugFeReadyHandler(
    INotificationPusher pusher,
    ILogger<DebugFeReadyHandler> logger)
{
    public async Task HandleAsync(DebugFeReadyMessage message, CancellationToken ct)
    {
        logger.LogInformation(
            "Received debug message | eventType={EventType} source={Source} correlation={CorrelationId}",
            message.EventType, message.Source, message.CorrelationId);

        await pusher.BroadcastEventAsync(
            "debug-fe-ready",
            new
            {
                eventType     = message.EventType,
                source        = message.Source,
                correlationId = message.CorrelationId,
                occurredAt    = message.OccurredAt,
                payload       = message.Payload,
                data          = message.Data
            },
            ct);
    }
}
