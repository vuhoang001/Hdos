using Hdos.DynamicFormService.Domain.Entities;

namespace Hdos.DynamicFormService.Domain.Repositories;

public interface IWidgetCatalogRepository
{
    Task<List<WidgetCatalog>> GetAllAsync(string? category, CancellationToken ct);
}
