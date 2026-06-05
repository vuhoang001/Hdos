using Hdos.LakehouseService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hdos.LakehouseService.Infrastructure.Sync;

public sealed class SyncStateRepository(LakehouseDbContext db) : ISyncStateRepository
{
    public Task<SyncState?> GetAsync(string viewName, CancellationToken ct) =>
        db.WarehouseSyncStates.AsNoTracking().FirstOrDefaultAsync(s => s.ViewName == viewName, ct);

    public Task<List<SyncState>> ListAsync(CancellationToken ct) =>
        db.WarehouseSyncStates.AsNoTracking().OrderBy(s => s.ViewName).ToListAsync(ct);

    public async Task UpsertAsync(string viewName, int rowCount, string jobId, CancellationToken ct)
    {
        var row = await db.WarehouseSyncStates.FirstOrDefaultAsync(s => s.ViewName == viewName, ct);
        var now = DateTime.UtcNow;

        if (row is null)
        {
            db.WarehouseSyncStates.Add(new SyncState
            {
                ViewName     = viewName,
                LastSyncedAt = now,
                LastRowCount = rowCount,
                LastJobId    = jobId,
            });
        }
        else
        {
            row.LastSyncedAt = now;
            row.LastRowCount = rowCount;
            row.LastJobId    = jobId;
        }

        await db.SaveChangesAsync(ct);
    }
}
