using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.DynamicFormService.Infrastructure.Persistence;

public sealed class WidgetCatalogRepository(DynamicFormDbContext db) : IWidgetCatalogRepository
{
    public Task<List<WidgetCatalog>> GetAllAsync(string? category, CancellationToken ct)
    {
        var query = db.WidgetCatalogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(w => w.Category == category);

        return query.OrderBy(w => w.SortOrder).ToListAsync(ct);
    }
}
