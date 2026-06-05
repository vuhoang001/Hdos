using Hdos.DynamicFormService.Domain.Entities;

namespace Hdos.DynamicFormService.Domain.Repositories;

public interface IFormModuleRepository
{
    Task<FormModule?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<FormModule?> GetByCodeAsync(string code, CancellationToken ct);
    Task<List<FormModule>> GetAllActiveAsync(CancellationToken ct);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken ct);
    Task AddAsync(FormModule module, CancellationToken ct);
    void Remove(FormModule module);
}
