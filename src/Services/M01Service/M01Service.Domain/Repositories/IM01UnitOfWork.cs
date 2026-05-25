namespace Hdos.M01Service.Domain.Repositories;

public interface IM01UnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
