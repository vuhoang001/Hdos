namespace Hdos.LakehouseService.Infrastructure.Sync;

public interface IWarehouseViewSyncer
{
    /// <summary>Danh sách tên VIEW mà syncer hỗ trợ.</summary>
    IReadOnlyCollection<string> SupportedViewNames { get; }

    /// <summary>
    /// Full-pull 1 VIEW từ warehouse external, publish mỗi row thành
    /// <c>LakehouseDataReadyIntegrationEvent</c>. Đồng thời cập nhật
    /// <see cref="SyncState"/> tracking last sync time + row count.
    /// </summary>
    Task<SyncResult> SyncAsync(string viewName, CancellationToken ct);
}

public sealed record SyncResult(string ViewName, int RowCount, string JobId, TimeSpan Duration);
