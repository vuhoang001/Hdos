using Hdos.SharedKernel;

namespace Hdos.AuthService.Domain.Entities;

public sealed class Role : BaseEntity<Guid>
{
    public string Name { get; private set; } = default!;
    public string Description { get; private set; } = string.Empty;

    public List<RolePermission> RolePermissions { get; private set; } = [];

    private Role() { }

    public static Role Create(string name, string description = "")
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Role name is required.");
        return new Role { Id = Guid.NewGuid(), Name = name.Trim(), Description = description.Trim() };
    }

    public void Update(string name, string description)
    {
        Name        = name.Trim();
        Description = description.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
