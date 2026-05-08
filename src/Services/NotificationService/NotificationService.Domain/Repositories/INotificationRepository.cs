using Hdos.NotificationService.Domain.Entities;

namespace Hdos.NotificationService.Domain.Repositories;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken ct);
    Task<IReadOnlyList<Notification>> ListRecentAsync(int take, CancellationToken ct);
    Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
