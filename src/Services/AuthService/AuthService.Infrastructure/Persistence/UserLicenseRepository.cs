using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.AuthService.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation của <see cref="IUserLicenseRepository"/>.
/// Scoped lifetime — được đăng ký trong <c>AuthInfrastructure.DependencyInjection</c>.
/// </summary>
public sealed class UserLicenseRepository : IUserLicenseRepository
{
    private readonly AuthDbContext _db;

    public UserLicenseRepository(AuthDbContext db) => _db = db;

    /// <inheritdoc/>
    public Task<UserLicense?> GetActiveByUserIdAsync(Guid userId, CancellationToken ct) =>
        _db.UserLicenses
            .Where(l => l.UserId == userId && l.IsActive)
            .OrderByDescending(l => l.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<UserLicense>> GetAllByUserIdAsync(Guid userId, CancellationToken ct) =>
        await _db.UserLicenses
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.CreatedAtUtc)
            .ToListAsync(ct);

    /// <inheritdoc/>
    public async Task AddAsync(UserLicense license, CancellationToken ct) =>
        await _db.UserLicenses.AddAsync(license, ct);

    /// <inheritdoc/>
    public void Update(UserLicense license) =>
        _db.UserLicenses.Update(license);
}
