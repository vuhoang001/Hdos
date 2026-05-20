using Hdos.OrderService.Application.DTOs;
using Hdos.OrderService.Domain.Entities;
using Hdos.OrderService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.OrderService.Application.Features.GetOrder;

public sealed record GetOrderByIdQuery(Guid OrderId) : IRequest<Result<OrderDto>>;

public sealed class GetOrderByIdQueryHandler(IOrderRepository orders)
    : IRequestHandler<GetOrderByIdQuery, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> Handle(GetOrderByIdQuery request, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(request.OrderId, ct);
        if (order is null) return Result.Failure<OrderDto>(Error.NotFound("Order"));
        return Map(order);
    }

    private static OrderDto Map(Order order) => new(
        order.Id,
        order.CustomerId,
        order.CustomerEmail,
        order.Status.ToString(),
        order.Total.Amount,
        order.Total.Currency,
        order.CreatedAtUtc,
        order.Items.Select(i =>
            new OrderItemDto(i.Id, i.ProductName, i.Quantity, i.UnitPrice.Amount, i.UnitPrice.Currency)
        ).ToList());
}
