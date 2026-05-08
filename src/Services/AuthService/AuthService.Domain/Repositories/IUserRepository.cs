using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.ValueObjects;

namespace Hdos.AuthService.Domain.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<User?> GetByEmailAsync(Email email, CancellationToken ct);
    Task<bool> ExistsByEmailAsync(Email email, CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
    void Update(User user);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
