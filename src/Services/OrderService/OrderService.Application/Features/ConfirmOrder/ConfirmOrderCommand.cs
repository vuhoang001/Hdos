using Hdos.OrderService.Domain.Entities;
using Hdos.OrderService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.OrderService.Application.Features.ConfirmOrder;

public sealed record ConfirmOrderCommand(Guid OrderId, OrderStatus Status) : IRequest<Result<Order>>;

public class ConfirmOrderCommandHandler(IOrderRepository orderRepository, IUnitOfWork uow)
    : IRequestHandler<ConfirmOrderCommand, Result<Order>>
{
    public async Task<Result<Order>> Handle(ConfirmOrderCommand request, CancellationToken ct)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, ct);
        if (order is null)
            return Result.Failure<Order>(new Error("NOTFOUND", "Không tìm thấy đơn hàng"));

        order.Confirm();
        await uow.SaveChangesAsync(ct);

        return Result.Success(order);
    }
}
