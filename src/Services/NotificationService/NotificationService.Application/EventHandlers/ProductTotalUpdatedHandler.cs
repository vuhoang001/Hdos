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
        logger.LogInformation("Broadcasting product stats: count={Count}, total={Total}",
            @event.TotalProductCount, @event.TotalProductsPrice);

        await pusher.BroadcastEventAsync(
            "product_total_updated",
            new
            {
                totalProductCount  = @event.TotalProductCount,
                totalProductsPrice = @event.TotalProductsPrice,
                averageProductPrice = @event.AverageProductPrice
            },
            ct);
    }
}
