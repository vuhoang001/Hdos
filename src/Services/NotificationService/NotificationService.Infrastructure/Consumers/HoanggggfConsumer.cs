using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.EventHandlers;
using MassTransit;

namespace Hdos.NotificationService.Infrastructure.Consumers;

public class HoanggggfConsumer(HoanggggfEventHandler handler) : IConsumer<HoanggggfIntegrationEvent>
{
    public async Task Consume(ConsumeContext<HoanggggfIntegrationEvent> context)
    {
        await handler.HandleAsync(context.Message, context.CancellationToken);
    }
}