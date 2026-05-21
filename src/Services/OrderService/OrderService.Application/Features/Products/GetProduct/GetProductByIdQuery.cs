using Hdos.OrderService.Application.DTOs;
using Hdos.OrderService.Domain.Entities;
using Hdos.OrderService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.OrderService.Application.Features.Products.GetProduct;

public sealed record GetProductByIdQuery(Guid ProductId) : IRequest<Result<ProductDto>>;

public sealed record GetAllProductsQuery : IRequest<Result<IReadOnlyList<ProductDto>>>;

public sealed class GetProductByIdQueryHandler(IProductRepository products)
    : IRequestHandler<GetProductByIdQuery, Result<ProductDto>>
{
    public async Task<Result<ProductDto>> Handle(GetProductByIdQuery request, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(request.ProductId, ct);
        if (product is null) return Result.Failure<ProductDto>(Error.NotFound("Product"));
        return Map(product);
    }

    private static ProductDto Map(Product p) =>
        new(p.Id, p.ProductId, p.ProductName!, p.ProductPrice);
}

public sealed class GetAllProductsQueryHandler(IProductRepository products)
    : IRequestHandler<GetAllProductsQuery, Result<IReadOnlyList<ProductDto>>>
{
    public async Task<Result<IReadOnlyList<ProductDto>>> Handle(GetAllProductsQuery request, CancellationToken ct)
    {
        var list = await products.ListAllAsync(ct);
        return Result.Success<IReadOnlyList<ProductDto>>(
            list.Select(p => new ProductDto(p.Id, p.ProductId, p.ProductName!, p.ProductPrice)).ToList());
    }
}
