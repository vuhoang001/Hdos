using Hdos.AuthService.Domain.Entities;

namespace Hdos.AuthService.Domain.Repositories;

public interface IUserLicenseRepository
{
    Task<UserLicense?> GetActiveByUserIdAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<UserLicense>> GetAllByUserIdAsync(Guid userId, CancellationToken ct);
    Task AddAsync(UserLicense license, CancellationToken ct);
    void Update(UserLicense license);
}
