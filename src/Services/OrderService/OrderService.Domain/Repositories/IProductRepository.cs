using Hdos.OrderService.Domain.Entities;

namespace Hdos.OrderService.Domain.Repositories;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Product>> ListAllAsync(CancellationToken ct);
    Task AddAsync(Product product, CancellationToken ct);
    void Update(Product product);
}
