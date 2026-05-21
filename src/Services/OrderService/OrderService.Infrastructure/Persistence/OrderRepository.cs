using Hdos.OrderService.Domain.Entities;
using Hdos.OrderService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;


namespace Hdos.OrderService.Infrastructure.Persistence;

public sealed class OrderRepository : IOrderRepository
{
    private readonly OrderDbContext _db;

    public OrderRepository(OrderDbContext db) => _db = db;

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyList<Order>> ListByCustomerAsync(Guid customerId, CancellationToken ct) =>
        await _db.Orders.Include(o => o.Items)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task AddAsync(Order order, CancellationToken ct) => await _db.Orders.AddAsync(order, ct);

    public void Update(Order order) => _db.Orders.Update(order);
}

public sealed class ProductRepository : IProductRepository
{
    private readonly OrderDbContext _db;

    public ProductRepository(OrderDbContext db) => _db = db;

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Product>> ListAllAsync(CancellationToken ct) =>
        await _db.Products.OrderBy(p => p.ProductName).ToListAsync(ct);

    public Task<decimal> GetTotalPriceAsync(CancellationToken ct) =>
        _db.Products.SumAsync(p => p.ProductPrice, ct);

    public Task<int> GetTotalCountAsync(CancellationToken ct) =>
        _db.Products.CountAsync(ct);

    public async Task AddAsync(Product product, CancellationToken ct) =>
        await _db.Products.AddAsync(product, ct);

    public void Update(Product product) => _db.Products.Update(product);
}

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly OrderDbContext _db;
    public UnitOfWork(OrderDbContext db) => _db = db;
    public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
