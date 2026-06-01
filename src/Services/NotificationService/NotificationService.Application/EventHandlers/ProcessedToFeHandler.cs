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
            "Received external message | eventType={EventType} source={Source} correlation={CorrelationId}",
            message.EventType, message.Source, message.CorrelationId);

        await pusher.BroadcastEventAsync(
            "processed-to-fe",
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
