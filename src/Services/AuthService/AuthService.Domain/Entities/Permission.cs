using Hdos.SharedKernel;

namespace Hdos.AuthService.Domain.Entities;

/// <summary>A single resource:action pair, e.g. "orders:create".</summary>
public sealed class Permission : BaseEntity<Guid>
{
    public string Resource { get; private set; } = default!;
    public string Action { get; private set; } = default!;
    public string Description { get; private set; } = string.Empty;

    /// <summary>Canonical key used in X-User-Permissions header and HdosPermissions constants.</summary>
    public string Key => $"{Resource}:{Action}";

    public List<RolePermission> RolePermissions { get; private set; } = [];

    private Permission() { }

    public static Permission Create(string resource, string action, string description = "")
    {
        if (string.IsNullOrWhiteSpace(resource)) throw new ArgumentException("Resource is required.");
        if (string.IsNullOrWhiteSpace(action))   throw new ArgumentException("Action is required.");
        return new Permission
        {
            Id          = Guid.NewGuid(),
            Resource    = resource.ToLowerInvariant().Trim(),
            Action      = action.ToLowerInvariant().Trim(),
            Description = description.Trim(),
        };
    }
}
