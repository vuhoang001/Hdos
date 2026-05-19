using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.EventHandlers;
using MassTransit;

namespace Hdos.NotificationService.Infrastructure.Consumers;

public sealed class OrderConfirmedConsumer(OrderConfirmedEventHandler handler)
    : IConsumer<OrderConfirmedIntegrationEvent>
{
    public Task Consume(ConsumeContext<OrderConfirmedIntegrationEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}