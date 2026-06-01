using Hdos.DynamicFormService.Domain.Repositories;

namespace Hdos.DynamicFormService.Infrastructure.Persistence;

public sealed class DynamicFormUnitOfWork(DynamicFormDbContext db) : IDynamicFormUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
