using Hdos.AuthService.Domain.Entities;

namespace Hdos.AuthService.Domain.Repositories;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Role?> GetByIdWithPermissionsAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct);
    Task<bool> ExistsByIdAsync(Guid id, CancellationToken ct);
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct);
    Task AddAsync(Role role, CancellationToken ct);
    void Update(Role role);
    void Delete(Role role);
}
