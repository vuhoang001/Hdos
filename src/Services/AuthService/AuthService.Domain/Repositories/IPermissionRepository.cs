using Hdos.AuthService.Domain.Entities;

namespace Hdos.AuthService.Domain.Repositories;

public interface IPermissionRepository
{
    Task<Permission?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken ct);
    Task<bool> ExistsByIdAsync(Guid id, CancellationToken ct);
    Task<bool> ExistsAsync(string resource, string action, CancellationToken ct);
    Task AddAsync(Permission permission, CancellationToken ct);
    void Delete(Permission permission);
}
