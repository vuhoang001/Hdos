using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.DynamicFormService.Infrastructure.Persistence;

public sealed class FormScreenRepository(DynamicFormDbContext db) : IFormScreenRepository
{
    public Task<FormScreen?> GetByIdAsync(Guid id, CancellationToken ct)
        => db.FormScreens.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<FormScreen?> GetByCodeAsync(string moduleCode, string screenCode, CancellationToken ct)
        => db.FormScreens.FirstOrDefaultAsync(s => s.ModuleCode == moduleCode && s.Code == screenCode, ct);

    public Task<FormScreen?> GetByCodeWithTabsAsync(string moduleCode, string screenCode, CancellationToken ct)
        => db.FormScreens
             .Include("Tabs.Widgets")
             .FirstOrDefaultAsync(s => s.ModuleCode == moduleCode && s.Code == screenCode, ct);

    public Task<FormScreen?> GetWithTabsAndWidgetsAsync(string moduleCode, string screenCode, CancellationToken ct)
        => db.FormScreens
             .Include("Tabs.Widgets")
             .FirstOrDefaultAsync(s => s.ModuleCode == moduleCode && s.Code == screenCode && s.Status == FormStatus.Published, ct);

    public Task<List<FormScreen>> GetByModuleAsync(string moduleCode, CancellationToken ct)
        => db.FormScreens
             .Where(s => s.ModuleCode == moduleCode)
             .OrderBy(s => s.SortOrder)
             .ToListAsync(ct);

    public Task<bool> ExistsByCodeAsync(string moduleCode, string screenCode, CancellationToken ct)
        => db.FormScreens.AnyAsync(s => s.ModuleCode == moduleCode && s.Code == screenCode, ct);

    public async Task<FormScreenTab?> GetTabWithWidgetsAsync(Guid screenId, Guid tabId, CancellationToken ct)
        => await db.FormScreenTabs
                   .Include(t => t.Widgets)
                   .FirstOrDefaultAsync(t => t.Id == tabId && t.ScreenId == screenId, ct);

    public Task<List<FormScreen>> GetPublishedByModuleAsync(string moduleCode, CancellationToken ct)
        => db.FormScreens
             .Where(s => s.ModuleCode == moduleCode && s.Status == FormStatus.Published)
             .OrderBy(s => s.SortOrder)
             .ToListAsync(ct);

    public async Task<Dictionary<string, int>> GetPublishedCountsAsync(IEnumerable<string> moduleCodes, CancellationToken ct)
    {
        var codes = moduleCodes.ToList();
        return await db.FormScreens
            .Where(s => codes.Contains(s.ModuleCode) && s.Status == FormStatus.Published)
            .GroupBy(s => s.ModuleCode)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }

    public void Add(FormScreen screen)    => db.FormScreens.Add(screen);
    public void Remove(FormScreen screen) => db.FormScreens.Remove(screen);
}
