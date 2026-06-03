using Hdos.DataMatchingService.Domain.Entities;

namespace Hdos.DataMatchingService.Domain.Repositories;

public interface IWidgetCatalogRepository
{
    Task<List<WidgetCatalog>> GetAllAsync(string? category, CancellationToken ct);
}
