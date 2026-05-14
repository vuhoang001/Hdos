using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.AuthService.Infrastructure.Persistence;

public sealed class PermissionRepository(AuthDbContext db) : IPermissionRepository
{
    public Task<Permission?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Permissions.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken ct) =>
        await db.Permissions.ToListAsync(ct);

    public Task<bool> ExistsByIdAsync(Guid id, CancellationToken ct) =>
        db.Permissions.AnyAsync(p => p.Id == id, ct);

    public Task<bool> ExistsAsync(string resource, string action, CancellationToken ct) =>
        db.Permissions.AnyAsync(p => p.Resource == resource && p.Action == action, ct);

    public async Task AddAsync(Permission permission, CancellationToken ct) =>
        await db.Permissions.AddAsync(permission, ct);

    public void Delete(Permission permission) => db.Permissions.Remove(permission);
}
