using FluentValidation;
using Hdos.OrderService.Application.Abstractions;
using Hdos.OrderService.Application.DTOs;
using Hdos.OrderService.Domain.Entities;
using Hdos.OrderService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.OrderService.Application.Features.CreateOrder;

public sealed record CreateOrderCommand(
    Guid CustomerId,
    string? CustomerEmail,
    IReadOnlyList<OrderItemInputDto> Items) : IRequest<Result<OrderDto>>;

public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductName).NotEmpty().MaximumLength(120);
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.UnitPrice).GreaterThanOrEqualTo(0);
            item.RuleFor(i => i.Currency).NotEmpty().Length(3);
        });
    }
}

public sealed class CreateOrderCommandHandler(
    IOrderRepository    orders,
    IUnitOfWork         uow,
    IUserLookupService  users)
    : IRequestHandler<CreateOrderCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        var lookup = await users.GetByIdAsync(request.CustomerId, ct);
        if (lookup.IsFailure)
            return Result.Failure<OrderDto>(lookup.Error);

        var lines = request.Items
            .Select(i => (i.ProductName, i.Quantity, i.UnitPrice, i.Currency));

        var order = Order.Create(request.CustomerId, lookup.Value.Email, lines);

        await orders.AddAsync(order, ct);
        await uow.SaveChangesAsync(ct);

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
