using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
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
        var users  = sp.GetRequiredService<IUserRepository>();
        var hasher = sp.GetRequiredService<IPasswordHasher<User>>();
        var config = sp.GetRequiredService<IConfiguration>();

        await SeedPermissionsAsync(db, ct);
        var (adminRole, userRole) = await SeedRolesAsync(db, ct);
        await db.SaveChangesAsync(ct);

        var adminUser = await SeedUserAsync(db, users, hasher, adminRole,
            email: "admin@hdos.dev",
            fullName: "Hdos Admin",
            password: config["Seed:AdminPassword"] ?? "Admin1234!",
            logger,
            ct);

        var testUser = await SeedUserAsync(db, users, hasher, userRole,
            email: "testuser@hdos.dev",
            fullName: "Test User",
            password: config["Seed:TestUserPassword"] ?? "Test1234!",
            logger,
            ct);

        await db.SaveChangesAsync(ct);

        await SeedLicenseAsync(db, adminUser, "enterprise", HdosModules.All, expiresAt: null, logger, ct);
        await SeedLicenseAsync(db, testUser, "basic",
            [HdosModules.Orders, HdosModules.Notifications],
            expiresAt: DateTime.UtcNow.AddYears(1), logger, ct);

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

    private static async Task<User> SeedUserAsync(
        AuthDbContext db,
        IUserRepository users,
        IPasswordHasher<User> hasher,
        Role role,
        string email,
        string fullName,
        string password,
        ILogger logger,
        CancellationToken ct)
    {
        var emailVo = Email.Create(email).Value!;
        var existing = await users.GetByEmailAsync(emailVo, ct);

        if (existing is null)
        {
            var u = User.Create(emailVo, fullName, passwordHash: "placeholder");
            u.SetPasswordHash(hasher.HashPassword(u, password));
            db.Users.Add(u);
            db.UserRoles.Add(UserRole.Assign(u.Id, role.Id));
            logger.LogInformation("Seeded new user {Email}", email);
            return u;
        }

        // User cũ có PasswordHash rỗng (vd JIT từ Keycloak trước migration) → set lại để login được.
        if (string.IsNullOrEmpty(existing.PasswordHash))
        {
            existing.SetPasswordHash(hasher.HashPassword(existing, password));
            users.Update(existing);
            logger.LogInformation("Re-seeded password for legacy user {Email}", email);
        }

        var assigned = await db.UserRoles.AnyAsync(ur => ur.UserId == existing.Id && ur.RoleId == role.Id, ct);
        if (!assigned)
        {
            db.UserRoles.Add(UserRole.Assign(existing.Id, role.Id));
            logger.LogInformation("Assigned role {Role} to existing user {Email}", role.Name, email);
        }

        return existing;
    }

    private static async Task SeedLicenseAsync(
        AuthDbContext db,
        User user,
        string plan,
        IEnumerable<string> modules,
        DateTime? expiresAt,
        ILogger logger,
        CancellationToken ct)
    {
        var hasActive = await db.UserLicenses.AnyAsync(l => l.UserId == user.Id && l.IsActive, ct);
        if (hasActive) return;

        db.UserLicenses.Add(UserLicense.Create(user.Id, plan, modules, expiresAt));
        logger.LogInformation("Seeded license plan={Plan} for {Email}", plan, user.Email.Value);
    }
}
