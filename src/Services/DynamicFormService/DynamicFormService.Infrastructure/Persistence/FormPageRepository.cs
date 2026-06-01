using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.DynamicFormService.Infrastructure.Persistence;

public sealed class FormPageRepository(DynamicFormDbContext db) : IFormPageRepository
{
    public Task<FormPage?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.FormPages.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<FormPage?> GetByCodeAsync(string moduleCode, string pageCode, CancellationToken ct = default)
        => db.FormPages.FirstOrDefaultAsync(
            p => p.ModuleCode == moduleCode && p.Code == pageCode, ct);

    public Task<List<FormPage>> GetByModuleAsync(string moduleCode, CancellationToken ct = default)
        => db.FormPages.Where(p => p.ModuleCode == moduleCode).ToListAsync(ct);

    public Task<bool> ExistsByCodeAsync(string moduleCode, string pageCode, CancellationToken ct = default)
        => db.FormPages.AnyAsync(p => p.ModuleCode == moduleCode && p.Code == pageCode, ct);

    public void Add(FormPage page) => db.FormPages.Add(page);
}
