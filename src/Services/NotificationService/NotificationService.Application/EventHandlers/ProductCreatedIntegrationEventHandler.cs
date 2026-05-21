using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.Realtime;
using Microsoft.Extensions.Logging;

namespace Hdos.NotificationService.Application.EventHandlers;

public class ProductCreatedIntegrationEventHandler(
    INotificationPusher pusher,
    ILogger<ProductCreatedIntegrationEventHandler> logger)
    : IIntegrationEventHandler<ProductCreatedIntegrationEvent>
{
    public async Task HandleAsync(ProductCreatedIntegrationEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Broadcasting product created: {ProductName}", @event.ProductName);

        await pusher.BroadcastEventAsync(
            "product_created",
            new { productId = @event.ProductId, productName = @event.ProductName, productPrice = @event.ProductPrice },
            ct);
    }
}