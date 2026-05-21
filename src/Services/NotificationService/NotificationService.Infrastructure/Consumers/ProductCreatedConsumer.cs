using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.EventHandlers;
using MassTransit;

namespace Hdos.NotificationService.Infrastructure.Consumers;

public class ProductCreatedConsumer(ProductCreatedIntegrationEventHandler handler)
    : IConsumer<ProductCreatedIntegrationEvent>
{
    public Task Consume(ConsumeContext<ProductCreatedIntegrationEvent> context)
    {
        return handler.HandleAsync(context.Message, context.CancellationToken);
    }
}