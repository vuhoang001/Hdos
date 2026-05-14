using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.AuthService.Infrastructure.Persistence;

public sealed class RoleRepository(AuthDbContext db) : IRoleRepository
{
    public Task<Role?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<Role?> GetByIdWithPermissionsAsync(Guid id, CancellationToken ct) =>
        db.Roles
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct) =>
        await db.Roles
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .ToListAsync(ct);

    public Task<bool> ExistsByIdAsync(Guid id, CancellationToken ct) =>
        db.Roles.AnyAsync(r => r.Id == id, ct);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken ct) =>
        db.Roles.AnyAsync(r => r.Name == name, ct);

    public async Task AddAsync(Role role, CancellationToken ct) =>
        await db.Roles.AddAsync(role, ct);

    public void Update(Role role) => db.Roles.Update(role);

    public void Delete(Role role) => db.Roles.Remove(role);
}
