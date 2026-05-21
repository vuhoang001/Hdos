using FluentValidation;
using Hdos.Common.Messaging;
using Hdos.OrderService.Application.DTOs;
using Hdos.OrderService.Domain.Entities;
using Hdos.OrderService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;
using Integration = Hdos.Contracts.IntegrationEvents;

namespace Hdos.OrderService.Application.Features.Products.CreateProduct;

public sealed record CreateProductCommand(
    string? ProductName,
    decimal ProductPrice) : IRequest<Result<ProductDto>>;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.ProductName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.ProductPrice).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateProductCommandHandler(
    IProductRepository products,
    IUnitOfWork uow,
    IEventBus eventBus)
    : IRequestHandler<CreateProductCommand, Result<ProductDto>>
{
    public async Task<Result<ProductDto>> Handle(CreateProductCommand request, CancellationToken ct)
    {
        var product = Product.Create(Guid.NewGuid(), request.ProductName, request.ProductPrice);

        await products.AddAsync(product, ct);
        await uow.SaveChangesAsync(ct);

        var totalPrice = await products.GetTotalPriceAsync(ct);

        await eventBus.PublishAsync(
            new Integration.ProductCreatedIntegrationEvent(
                product.ProductId,
                product.ProductName!,
                product.ProductPrice,
                totalPrice),
            ct);

        return Map(product);
    }

    private static ProductDto Map(Product p) =>
        new(p.Id, p.ProductId, p.ProductName!, p.ProductPrice);
}
