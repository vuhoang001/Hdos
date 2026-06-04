using Hdos.LakehouseService.Domain.Entities;
using Hdos.LakehouseService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.LakehouseService.Infrastructure.Persistence.Repositories;

public sealed class LakehouseSnapshotRepository(LakehouseDbContext db) : ILakehouseSnapshotRepository
{
    public async Task AddAsync(LakehouseSnapshot snapshot, CancellationToken ct) =>
        await db.Snapshots.AddAsync(snapshot, ct);

    public async Task AddRangeAsync(IEnumerable<LakehouseSnapshot> snapshots, CancellationToken ct) =>
        await db.Snapshots.AddRangeAsync(snapshots, ct);

    public Task<LakehouseSnapshot?> GetLatestAsync(string @namespace, string businessKey, CancellationToken ct) =>
        db.Snapshots
            .Where(s => s.Namespace == @namespace && s.BusinessKey == businessKey)
            .OrderByDescending(s => s.ReceivedAt)
            .FirstOrDefaultAsync(ct);

    public Task<List<LakehouseSnapshot>> GetByNamespaceAsync(string @namespace, int limit, CancellationToken ct) =>
        db.Snapshots
            .Where(s => s.Namespace == @namespace)
            .OrderByDescending(s => s.ReceivedAt)
            .Take(limit)
            .ToListAsync(ct);

    public Task<int> SaveChangesAsync(CancellationToken ct) =>
        db.SaveChangesAsync(ct);
}
