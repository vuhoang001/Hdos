namespace Hdos.DataMatchingService.Domain.Repositories;

public interface IDataMatchingUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
