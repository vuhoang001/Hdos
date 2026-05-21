using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.Realtime;
using Microsoft.Extensions.Logging;

namespace Hdos.NotificationService.Application.EventHandlers;

public sealed class ProductTotalUpdatedHandler(
    INotificationPusher pusher,
    ILogger<ProductTotalUpdatedHandler> logger)
    : IIntegrationEventHandler<ProductCreatedIntegrationEvent>
{
    public async Task HandleAsync(ProductCreatedIntegrationEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Broadcasting product total updated: {Total}", @event.TotalProductsPrice);

        await pusher.BroadcastEventAsync(
            "product_total_updated",
            new { totalProductsPrice = @event.TotalProductsPrice },
            ct);
    }
}

