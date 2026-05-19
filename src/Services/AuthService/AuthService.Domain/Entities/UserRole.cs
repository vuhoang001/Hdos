using Hdos.SharedKernel;

namespace Hdos.AuthService.Domain.Entities;

/// <summary>Gán user vào một role (many-to-many).</summary>
public sealed class UserRole : BaseEntity<Guid>
{
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }

    public Role Role { get; private set; } = default!;

    private UserRole() { }

    public static UserRole Assign(Guid userId, Guid roleId)
        => new() { Id = Guid.NewGuid(), UserId = userId, RoleId = roleId };
}
