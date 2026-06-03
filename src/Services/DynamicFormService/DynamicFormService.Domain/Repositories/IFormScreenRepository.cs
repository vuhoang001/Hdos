using Hdos.DynamicFormService.Domain.Entities;

namespace Hdos.DynamicFormService.Domain.Repositories;

public interface IFormScreenRepository
{
    Task<FormScreen?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<FormScreen?> GetByCodeAsync(string moduleCode, string screenCode, CancellationToken ct = default);
    Task<FormScreen?> GetByCodeWithTabsAsync(string moduleCode, string screenCode, CancellationToken ct = default);
    Task<FormScreen?> GetWithTabsAndWidgetsAsync(string moduleCode, string screenCode, CancellationToken ct = default);
    Task<List<FormScreen>> GetByModuleAsync(string moduleCode, CancellationToken ct = default);
    Task<bool> ExistsByCodeAsync(string moduleCode, string screenCode, CancellationToken ct = default);
    Task<FormScreenTab?> GetTabWithWidgetsAsync(Guid screenId, Guid tabId, CancellationToken ct = default);
    void Add(FormScreen screen);
    void Remove(FormScreen screen);
}
