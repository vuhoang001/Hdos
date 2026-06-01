using Hdos.DynamicFormService.Domain.Entities;

namespace Hdos.DynamicFormService.Domain.Repositories;

public interface IFormTemplateRepository
{
    Task<FormTemplate?> GetByIdAsync(Guid id, bool includeFields, CancellationToken ct);
    Task<FormTemplate?> GetByModuleAndKeyAsync(string moduleCode, string formKey, bool includeFields, CancellationToken ct);
    Task<List<FormTemplate>> GetByModuleCodeAsync(string moduleCode, CancellationToken ct);
    Task<bool> ExistsByKeyInModuleAsync(Guid moduleId, string key, CancellationToken ct);
    Task AddAsync(FormTemplate template, CancellationToken ct);
}
