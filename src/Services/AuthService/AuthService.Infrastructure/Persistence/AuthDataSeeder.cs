using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.ValueObjects;
using Hdos.Common.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hdos.AuthService.Infrastructure.Persistence;

/// <summary>
/// Seed dữ liệu ban đầu cho AuthService:
/// - Permissions từ HdosPermissions constants
/// - Roles "admin" (mọi permission) + "user" (read-only subset)
/// - Users admin@hdos.dev + testuser@hdos.dev (gắn role tương ứng)
///
/// Idempotent: chỉ thêm khi chưa tồn tại; password mặc định lấy từ env, fallback default.
/// </summary>
public static class AuthDataSeeder
{
    public static async Task SeedAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp     = scope.ServiceProvider;
        var db     = sp.GetRequiredService<AuthDbContext>();
        var hasher = sp.GetRequiredService<IPasswordHasher<User>>();
        var config = sp.GetRequiredService<IConfiguration>();

        await SeedPermissionsAsync(db, ct);
        var (adminRole, userRole) = await SeedRolesAsync(db, ct);
        await db.SaveChangesAsync(ct);

        await SeedUserAsync(db, hasher, adminRole,
            email: "admin@hdos.dev",
            fullName: "Hdos Admin",
            password: config["Seed:AdminPassword"] ?? "Admin1234!",
            ct);

        await SeedUserAsync(db, hasher, userRole,
            email: "testuser@hdos.dev",
            fullName: "Test User",
            password: config["Seed:TestUserPassword"] ?? "Test1234!",
            ct);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("AuthDataSeeder: seed hoàn tất");
    }

    private static async Task SeedPermissionsAsync(AuthDbContext db, CancellationToken ct)
    {
        var allKeys = HdosPermissions.All;
        var existing = await db.Permissions.Select(p => p.Resource + ":" + p.Action).ToListAsync(ct);
        var missing = allKeys.Except(existing).ToList();
        if (missing.Count == 0) return;

        foreach (var key in missing)
        {
            var parts = key.Split(':', 2);
            db.Permissions.Add(Permission.Create(parts[0], parts.Length > 1 ? parts[1] : "", $"Auto-seeded {key}"));
        }
    }

    private static async Task<(Role admin, Role user)> SeedRolesAsync(AuthDbContext db, CancellationToken ct)
    {
        var admin = await db.Roles.FirstOrDefaultAsync(r => r.Name == "admin", ct);
        if (admin is null)
        {
            admin = Role.Create("admin", "Quản trị viên — toàn quyền");
            db.Roles.Add(admin);
        }
        var user = await db.Roles.FirstOrDefaultAsync(r => r.Name == "user", ct);
        if (user is null)
        {
            user = Role.Create("user", "Người dùng thông thường");
            db.Roles.Add(user);
        }
        await db.SaveChangesAsync(ct);

        // Wire permissions
        var allPerms = await db.Permissions.ToListAsync(ct);
        await EnsureRolePermissionsAsync(db, admin, allPerms.Select(p => p.Id), ct);

        var readSubset = allPerms
            .Where(p => p.Action == "read")
            .Select(p => p.Id);
        await EnsureRolePermissionsAsync(db, user, readSubset, ct);

        return (admin, user);
    }

    private static async Task EnsureRolePermissionsAsync(AuthDbContext db, Role role, IEnumerable<Guid> permIds, CancellationToken ct)
    {
        var existing = await db.RolePermissions
            .Where(rp => rp.RoleId == role.Id)
            .Select(rp => rp.PermissionId)
            .ToListAsync(ct);

        foreach (var pid in permIds.Except(existing))
            db.RolePermissions.Add(RolePermission.Create(role.Id, pid));
    }

    private static async Task SeedUserAsync(
        AuthDbContext db,
        IPasswordHasher<User> hasher,
        Role role,
        string email,
        string fullName,
        string password,
        CancellationToken ct)
    {
        var emailVo = Email.Create(email).Value!;
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email.Value == emailVo.Value, ct);
        if (existing is null)
        {
            var u = User.Create(emailVo, fullName, passwordHash: "placeholder");
            u.SetPasswordHash(hasher.HashPassword(u, password));
            db.Users.Add(u);
            db.UserRoles.Add(UserRole.Assign(u.Id, role.Id));
            return;
        }

        // User cũ (vd JIT từ Keycloak) có thể có PasswordHash rỗng — set lại để login được.
        if (string.IsNullOrEmpty(existing.PasswordHash))
            existing.SetPasswordHash(hasher.HashPassword(existing, password));

        var assigned = await db.UserRoles.AnyAsync(ur => ur.UserId == existing.Id && ur.RoleId == role.Id, ct);
        if (!assigned)
            db.UserRoles.Add(UserRole.Assign(existing.Id, role.Id));
    }
}
