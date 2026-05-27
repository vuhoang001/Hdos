using Hdos.DataMatchingService.Domain.Repositories;

namespace Hdos.DataMatchingService.Infrastructure.Persistence;

public sealed class DataMatchingUnitOfWork(DataMatchingDbContext db) : IDataMatchingUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
