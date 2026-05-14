using Hdos.AuthService.Domain.Entities;

namespace Hdos.AuthService.Domain.Repositories;

public interface IUserRoleRepository
{
    /// <summary>Returns all roles (with their permissions) assigned to a user.</summary>
    Task<IReadOnlyList<Role>> GetRolesWithPermissionsAsync(Guid userId, CancellationToken ct);

    Task<UserRole?> GetAsync(Guid userId, Guid roleId, CancellationToken ct);
    Task<IReadOnlyList<UserRole>> GetByUserIdAsync(Guid userId, CancellationToken ct);
    Task<bool> ExistsAsync(Guid userId, Guid roleId, CancellationToken ct);
    Task AddAsync(UserRole userRole, CancellationToken ct);
    void Remove(UserRole userRole);
}
