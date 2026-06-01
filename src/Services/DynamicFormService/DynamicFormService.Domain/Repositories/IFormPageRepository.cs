using Hdos.DynamicFormService.Domain.Entities;

namespace Hdos.DynamicFormService.Domain.Repositories;

public interface IFormPageRepository
{
    Task<FormPage?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<FormPage?> GetByCodeAsync(string moduleCode, string pageCode, CancellationToken ct = default);
    Task<List<FormPage>> GetByModuleAsync(string moduleCode, CancellationToken ct = default);
    Task<bool> ExistsByCodeAsync(string moduleCode, string pageCode, CancellationToken ct = default);
    void Add(FormPage page);
}
