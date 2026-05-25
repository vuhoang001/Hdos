using FluentValidation;
using Hdos.Common.Messaging;
using Hdos.OrderService.Application.Abstractions;
using Hdos.OrderService.Application.DTOs;
using Hdos.OrderService.Domain.Entities;
using Hdos.OrderService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;
using Integration = Hdos.Contracts.IntegrationEvents;

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

public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Result<OrderDto>>
{
    private readonly IOrderRepository   _orders;
    private readonly IUnitOfWork        _uow;
    private readonly IEventBus          _eventBus;
    private readonly IUserLookupService _users;

    public CreateOrderCommandHandler(
        IOrderRepository orders,
        IUnitOfWork uow,
        IEventBus eventBus,
        IUserLookupService users)
    {
        _orders   = orders;
        _uow      = uow;
        _eventBus = eventBus;
        _users    = users;
    }

    public async Task<Result<OrderDto>> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        // Cross-service sync call: ask AuthService over gRPC whether the customer exists.
        // We also use the email returned from Auth as the source of truth — caller's email
        // (if any) is ignored to avoid drift between services.
        var lookup = await _users.GetByIdAsync(request.CustomerId, ct);
        if (lookup.IsFailure)
            return Result.Failure<OrderDto>(lookup.Error);

        var lines = request.Items
            .Select(i => (i.ProductName, i.Quantity, i.UnitPrice, i.Currency));

        var order = Order.Create(request.CustomerId, lookup.Value.Email, lines);

        await _orders.AddAsync(order, ct);

        var integrationItems = order.Items
            .Select(i => new Integration.OrderItemDto(i.ProductName, i.Quantity, i.UnitPrice.Amount))
            .ToList();

        // Publish trước SaveChanges: outbox ghi Order + OutboxMessage trong 1 transaction
        await _eventBus.PublishAsync(
            new Integration.OrderCreatedIntegrationEvent(
                order.Id,
                order.CustomerId,
                order.CustomerEmail,
                order.Total.Amount,
                integrationItems),
            ct);

        await _uow.SaveChangesAsync(ct);

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
                               new OrderItemDto(i.Id, i.ProductName, i.Quantity, i.UnitPrice.Amount,
                                                i.UnitPrice.Currency)
        ).ToList());
}