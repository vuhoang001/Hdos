using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.DynamicFormService.Infrastructure.Persistence;

public sealed class ProviderRepository(DynamicFormDbContext db) : IProviderRepository
{
    public async Task AddAsync(Provider provider, CancellationToken ct)
        => await db.Providers.AddAsync(provider, ct);

    public Task<Provider?> GetByIdAsync(Guid id, CancellationToken ct)
        => db.Providers.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Provider?> GetByCodeAsync(string code, CancellationToken ct)
        => db.Providers.FirstOrDefaultAsync(p => p.Code == code, ct);

    public Task<List<Provider>> GetAllAsync(ProviderStatus? status, CancellationToken ct)
    {
        var q = db.Providers.AsQueryable();
        if (status.HasValue) q = q.Where(p => p.Status == status.Value);
        return q.OrderBy(p => p.Code).ToListAsync(ct);
    }

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken ct)
        => db.Providers.AnyAsync(p => p.Code == code, ct);

    public void Remove(Provider provider)
        => db.Providers.Remove(provider);
}
