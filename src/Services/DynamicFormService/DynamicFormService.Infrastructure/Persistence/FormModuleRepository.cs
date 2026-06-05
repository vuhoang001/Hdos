using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.DynamicFormService.Infrastructure.Persistence;

public sealed class FormModuleRepository(DynamicFormDbContext db) : IFormModuleRepository
{
    public Task<FormModule?> GetByIdAsync(Guid id, CancellationToken ct)
        => db.FormModules.FirstOrDefaultAsync(m => m.Id == id, ct);

    public Task<FormModule?> GetByCodeAsync(string code, CancellationToken ct)
        => db.FormModules.FirstOrDefaultAsync(m => m.Code == code, ct);

    public Task<List<FormModule>> GetAllActiveAsync(CancellationToken ct)
        => db.FormModules
            .Where(m => m.Status == ModuleStatus.Active).OrderBy(m => m.Name).ToListAsync(ct);

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken ct)
        => db.FormModules.AnyAsync(m => m.Code == code, ct);

    public async Task AddAsync(FormModule module, CancellationToken ct)
        => await db.FormModules.AddAsync(module, ct);

    public void Remove(FormModule module)
        => db.FormModules.Remove(module);
}
