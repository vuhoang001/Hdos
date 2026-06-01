namespace Hdos.DynamicFormService.Domain.Repositories;

public interface IDynamicFormUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
