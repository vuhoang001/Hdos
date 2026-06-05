namespace Hdos.LakehouseService.Infrastructure.Sync;

public interface ISyncStateRepository
{
    Task<SyncState?> GetAsync(string viewName, CancellationToken ct);
    Task<List<SyncState>> ListAsync(CancellationToken ct);
    Task UpsertAsync(string viewName, int rowCount, string jobId, CancellationToken ct);
}
