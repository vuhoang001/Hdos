using Hdos.SharedKernel;

namespace Hdos.AuthService.Domain.Entities;

/// <summary>
/// License gắn với một user. Modules được lưu dạng CSV ("orders,m01,forms").
/// Plan: free | basic | pro | enterprise.
/// </summary>
public sealed class UserLicense : BaseEntity<Guid>
{
    public Guid UserId { get; private set; }
    public string Plan { get; private set; } = default!;
    public string ModulesCsv { get; private set; } = string.Empty;
    public DateTime? ExpiresAtUtc { get; private set; }
    public bool IsActive { get; private set; }

    private UserLicense() { }

    public static UserLicense Create(
        Guid userId,
        string plan,
        IEnumerable<string> modules,
        DateTime? expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(plan))
            throw new ArgumentException("Plan là bắt buộc.", nameof(plan));

        return new UserLicense
        {
            Id           = Guid.NewGuid(),
            UserId       = userId,
            Plan         = plan.Trim().ToLowerInvariant(),
            ModulesCsv   = string.Join(',', modules.Select(m => m.Trim().ToLowerInvariant()).Distinct()),
            ExpiresAtUtc = expiresAtUtc,
            IsActive     = true,
        };
    }

    public void Update(string plan, IEnumerable<string> modules, DateTime? expiresAtUtc)
    {
        Plan         = plan.Trim().ToLowerInvariant();
        ModulesCsv   = string.Join(',', modules.Select(m => m.Trim().ToLowerInvariant()).Distinct());
        ExpiresAtUtc = expiresAtUtc;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Revoke()
    {
        IsActive     = false;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public IReadOnlyList<string> GetModules() =>
        ModulesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public bool IsExpired() =>
        ExpiresAtUtc.HasValue && ExpiresAtUtc.Value < DateTime.UtcNow;
}
