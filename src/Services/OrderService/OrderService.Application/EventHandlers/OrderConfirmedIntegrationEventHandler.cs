using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.OrderService.Domain.Events;
using MediatR;

namespace Hdos.OrderService.Application.EventHandlers;

public sealed class OrderConfirmedIntegrationEventHandler(IEventBus eventBus)
    : INotificationHandler<OrderConfirmedDomainEvent>
{
    public async Task Handle(OrderConfirmedDomainEvent notification, CancellationToken ct)
    {
        await eventBus.PublishAsync(
            new OrderConfirmedIntegrationEvent(
                notification.OrderId,
                notification.CustomerId,
                notification.CustomerEmail,
                notification.Status),
            ct);
    }
}
