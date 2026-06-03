using Hdos.DataMatchingService.Domain.Entities;
using Hdos.DataMatchingService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.DataMatchingService.Infrastructure.Persistence;

public sealed class WidgetCatalogRepository(DataMatchingDbContext db) : IWidgetCatalogRepository
{
    public Task<List<WidgetCatalog>> GetAllAsync(string? category, CancellationToken ct)
    {
        var query = db.WidgetCatalogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(w => w.Category == category);

        return query.OrderBy(w => w.SortOrder).ToListAsync(ct);
    }
}
