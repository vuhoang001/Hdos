using Hdos.NotificationService.Domain.Entities;
using Hdos.NotificationService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.NotificationService.Infrastructure.Persistence;

public sealed class NotificationRepository(NotificationDbContext db) : INotificationRepository
{
    public async Task AddAsync(Notification notification, CancellationToken ct) =>
        await db.Notifications.AddAsync(notification, ct);

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Notifications.FirstOrDefaultAsync(n => n.Id == id, ct);

    public async Task<IReadOnlyList<Notification>> ListRecentAsync(int take, CancellationToken ct) =>
        await db.Notifications
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);
}

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly NotificationDbContext _db;
    public UnitOfWork(NotificationDbContext db) => _db = db;
    public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
