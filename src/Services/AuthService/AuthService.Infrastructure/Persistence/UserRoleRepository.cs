using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.AuthService.Infrastructure.Persistence;

public sealed class UserRoleRepository(AuthDbContext db) : IUserRoleRepository
{
    public async Task<IReadOnlyList<Role>> GetRolesWithPermissionsAsync(Guid userId, CancellationToken ct) =>
        await db.Roles
            .Where(r => db.UserRoles.Any(ur => ur.UserId == userId && ur.RoleId == r.Id))
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .ToListAsync(ct);

    public Task<UserRole?> GetAsync(Guid userId, Guid roleId, CancellationToken ct) =>
        db.UserRoles.FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId, ct);

    public async Task<IReadOnlyList<UserRole>> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
        await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Include(ur => ur.Role)
            .ToListAsync(ct);

    public Task<bool> ExistsAsync(Guid userId, Guid roleId, CancellationToken ct) =>
        db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId, ct);

    public async Task AddAsync(UserRole userRole, CancellationToken ct) =>
        await db.UserRoles.AddAsync(userRole, ct);

    public void Remove(UserRole userRole) => db.UserRoles.Remove(userRole);
}
