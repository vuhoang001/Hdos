using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.Realtime;
using Microsoft.Extensions.Logging;

namespace Hdos.NotificationService.Application.EventHandlers;

public sealed class DashboardFeReadyHandler(
    INotificationPusher pusher,
    ILogger<DashboardFeReadyHandler> logger)
    : IIntegrationEventHandler<DashboardFeReadyIntegrationEvent>
{
    public async Task HandleAsync(DashboardFeReadyIntegrationEvent @event, CancellationToken ct)
    {
        logger.LogInformation(
            "Broadcasting dashboard-fe-ready: {Count} records processed, systems=[{Systems}]",
            @event.ProcessedCount,
            string.Join(", ", @event.AffectedSystems));

        await pusher.BroadcastEventAsync(
            "dashboard-fe-ready",
            new
            {
                processedCount  = @event.ProcessedCount,
                affectedSystems = @event.AffectedSystems
            },
            ct);
    }
}
