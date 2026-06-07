using Hdos.LakehouseService.Domain.Entities;
using Hdos.LakehouseService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.LakehouseService.Infrastructure.Persistence.Repositories;

public sealed class ViewBindingRepository(LakehouseDbContext db) : IViewBindingRepository
{
    public Task<ViewBinding?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.ViewBindings.FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<ViewBinding?> GetByViewNameAsync(string viewName, CancellationToken ct) =>
        db.ViewBindings.FirstOrDefaultAsync(b => b.ViewName == viewName, ct);

    public Task<List<ViewBinding>> ListAsync(CancellationToken ct) =>
        db.ViewBindings.AsNoTracking()
                       .OrderBy(b => b.ViewName)
                       .ToListAsync(ct);

    public Task<List<ViewBinding>> ListActiveAsync(CancellationToken ct) =>
        db.ViewBindings.AsNoTracking()
                       .Where(b => b.IsActive)
                       .OrderBy(b => b.ViewName)
                       .ToListAsync(ct);

    public async Task AddAsync(ViewBinding binding, CancellationToken ct)
    {
        await db.ViewBindings.AddAsync(binding, ct);
    }

    public Task RemoveAsync(ViewBinding binding, CancellationToken ct)
    {
        db.ViewBindings.Remove(binding);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
