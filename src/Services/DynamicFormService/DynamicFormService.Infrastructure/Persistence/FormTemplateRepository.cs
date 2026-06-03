using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.DynamicFormService.Infrastructure.Persistence;

public sealed class FormTemplateRepository(DynamicFormDbContext db) : IFormTemplateRepository
{
    public Task<FormTemplate?> GetByIdAsync(Guid id, bool includeFields, CancellationToken ct)
    {
        var q = db.FormTemplates.AsQueryable();
        if (includeFields) q = q.Include(t => t.Fields);
        return q.FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public Task<FormTemplate?> GetByModuleAndKeyAsync(string moduleCode, string formKey, bool includeFields, CancellationToken ct)
    {
        var q = db.FormTemplates.AsQueryable();
        if (includeFields) q = q.Include(t => t.Fields);
        return q.FirstOrDefaultAsync(t => t.ModuleCode == moduleCode && t.Key == formKey, ct);
    }

    public Task<List<FormTemplate>> GetByModuleCodeAsync(string moduleCode, CancellationToken ct)
        => db.FormTemplates
            .Include(t => t.Fields)
            .Where(t => t.ModuleCode == moduleCode)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

    public Task<bool> ExistsByKeyInModuleAsync(Guid moduleId, string key, CancellationToken ct)
        => db.FormTemplates.AnyAsync(t => t.ModuleId == moduleId && t.Key == key, ct);

    public async Task AddAsync(FormTemplate template, CancellationToken ct)
        => await db.FormTemplates.AddAsync(template, ct);

    public async Task<Dictionary<string, int>> GetPublishedCountsAsync(IEnumerable<string> moduleCodes, CancellationToken ct)
    {
        var codes = moduleCodes.ToList();
        return await db.FormTemplates
            .Where(t => codes.Contains(t.ModuleCode) && t.Status == FormStatus.Published)
            .GroupBy(t => t.ModuleCode)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }
}
