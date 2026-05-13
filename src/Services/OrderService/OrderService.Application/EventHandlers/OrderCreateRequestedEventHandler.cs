using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.OrderService.Application.DTOs;
using Hdos.OrderService.Application.Features.CreateOrder;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Hdos.OrderService.Application.EventHandlers;

public sealed class OrderCreateRequestedEventHandler(
    ISender sender,
    ILogger<OrderCreateRequestedEventHandler> logger)
    : IIntegrationEventHandler<OrderCreateRequestedIntegrationEvent>
{
    public async Task HandleAsync(OrderCreateRequestedIntegrationEvent @event, CancellationToken ct)
    {
        logger.LogInformation(
            "Processing async order creation. CorrelationId={CorrelationId} CustomerId={CustomerId}",
            @event.CorrelationId, @event.CustomerId);

        var items = @event.Items
            .Select(i => new OrderItemInputDto(i.ProductName, i.Quantity, i.UnitPrice, "VND"))
            .ToList();

        var result = await sender.Send(
            new CreateOrderCommand(@event.CustomerId, null, items), ct);

        if (result.IsFailure)
            logger.LogWarning(
                "Async order creation failed. CorrelationId={CorrelationId} Error={Error}",
                @event.CorrelationId, result.Error.Message);
        else
            logger.LogInformation(
                "Async order created. CorrelationId={CorrelationId} OrderId={OrderId}",
                @event.CorrelationId, result.Value.Id);
    }
}
