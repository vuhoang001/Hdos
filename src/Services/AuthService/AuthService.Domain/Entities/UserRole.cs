using Hdos.SharedKernel;

namespace Hdos.AuthService.Domain.Entities;

/// <summary>Assigns a Keycloak user (by sub/Guid) to an RBAC role.</summary>
public sealed class UserRole : BaseEntity<Guid>
{
    /// <summary>Keycloak sub claim (matches User.Id in profile store).</summary>
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }

    public Role Role { get; private set; } = default!;

    private UserRole() { }

    public static UserRole Assign(Guid userId, Guid roleId)
        => new() { Id = Guid.NewGuid(), UserId = userId, RoleId = roleId };
}
