using Hdos.Contracts.IntegrationEvents;
using Hdos.OrderService.Application.EventHandlers;
using MassTransit;

namespace Hdos.OrderService.Infrastructure.Consumers;

public sealed class OrderCreateRequestedConsumer(OrderCreateRequestedEventHandler handler)
    : IConsumer<OrderCreateRequestedIntegrationEvent>
{
    public Task Consume(ConsumeContext<OrderCreateRequestedIntegrationEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
