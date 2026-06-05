namespace Hdos.LakehouseService.Infrastructure.Sync;

public sealed class SyncState
{
    public string ViewName { get; set; } = default!;
    public DateTime LastSyncedAt { get; set; }
    public int LastRowCount { get; set; }
    public string? LastJobId { get; set; }
}
