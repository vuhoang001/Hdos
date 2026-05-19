using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.OrderService.Domain.Events;
using MediatR;

namespace Hdos.OrderService.Application.EventHandlers;

// Domain event handler: chạy sau SaveChanges, publish integration event lên RabbitMQ
public sealed class OrderConfirmedEventHandler(IEventBus eventBus)
    : INotificationHandler<OrderConfirmedDomainEvent>
{
    public async Task Handle(OrderConfirmedDomainEvent notification, CancellationToken cancellationToken)
    {
        await eventBus.PublishAsync(
            new OrderConfirmedIntegrationEvent(notification.OrderId, notification.CustomerId, notification.CustomerEmail, notification.Status),
            cancellationToken);
    }
}