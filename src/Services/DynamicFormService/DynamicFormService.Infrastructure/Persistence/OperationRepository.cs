using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.DynamicFormService.Infrastructure.Persistence;

public sealed class OperationRepository(DynamicFormDbContext db) : IOperationRepository
{
    public async Task AddAsync(Operation operation, CancellationToken ct)
        => await db.Operations.AddAsync(operation, ct);

    public Task<Operation?> GetByIdAsync(Guid id, CancellationToken ct)
        => db.Operations.FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<Operation?> GetByKeyAsync(string providerCode, string operationKey, CancellationToken ct)
        => db.Operations.FirstOrDefaultAsync(
            o => o.ProviderCode == providerCode && o.OperationKey == operationKey, ct);

    public Task<List<Operation>> GetByProviderAsync(string providerCode, CancellationToken ct)
        => db.Operations
            .Where(o => o.ProviderCode == providerCode)
            .OrderBy(o => o.OperationKey)
            .ToListAsync(ct);

    public Task<List<Operation>> GetAllAsync(OperationStatus? status, CancellationToken ct)
    {
        var q = db.Operations.AsQueryable();
        if (status.HasValue) q = q.Where(o => o.Status == status.Value);
        return q.OrderBy(o => o.ProviderCode).ThenBy(o => o.OperationKey).ToListAsync(ct);
    }

    public Task<bool> ExistsByKeyAsync(string providerCode, string operationKey, CancellationToken ct)
        => db.Operations.AnyAsync(
            o => o.ProviderCode == providerCode && o.OperationKey == operationKey, ct);

    public Task<bool> AnyByProviderAsync(string providerCode, CancellationToken ct)
        => db.Operations.AnyAsync(o => o.ProviderCode == providerCode, ct);

    public void Remove(Operation operation)
        => db.Operations.Remove(operation);
}
