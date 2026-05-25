using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.OrderService.Domain.Events;
using MediatR;

namespace Hdos.OrderService.Application.EventHandlers;

public sealed class OrderCreatedIntegrationEventHandler(IEventBus eventBus)
    : INotificationHandler<OrderCreatedDomainEvent>
{
    public async Task Handle(OrderCreatedDomainEvent notification, CancellationToken ct)
    {
        var items = notification.Items
            .Select(i => new OrderItemDto(i.ProductName, i.Quantity, i.UnitPrice))
            .ToList();

        await eventBus.PublishAsync(
            new OrderCreatedIntegrationEvent(
                notification.OrderId,
                notification.CustomerId,
                notification.CustomerEmail,
                notification.TotalAmount,
                items),
            ct);
    }
}
