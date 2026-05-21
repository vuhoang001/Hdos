using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.EventHandlers;
using MassTransit;

namespace Hdos.NotificationService.Infrastructure.Consumers;

public sealed class ProductTotalUpdatedConsumer(ProductTotalUpdatedHandler handler)
    : IConsumer<ProductCreatedIntegrationEvent>
{
    public Task Consume(ConsumeContext<ProductCreatedIntegrationEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
