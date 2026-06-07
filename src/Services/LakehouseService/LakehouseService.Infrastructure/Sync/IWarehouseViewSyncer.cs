namespace Hdos.LakehouseService.Infrastructure.Sync;

public interface IWarehouseViewSyncer
{
    /// <summary>
    /// Pull toàn bộ row của VIEW theo 1 binding cụ thể, publish mỗi row thành
    /// <c>RawRecordIngestRequestedIntegrationEvent</c> để DataMatchingService consume.
    /// Cập nhật <see cref="SyncState"/> theo <c>ViewName</c> của binding.
    /// </summary>
    Task<SyncResult> SyncAsync(Guid bindingId, CancellationToken ct);

    /// <summary>
    /// Pull tất cả binding đang active. Lỗi 1 binding không stop các binding còn lại —
    /// mỗi binding sync độc lập, kết quả thu về list.
    /// </summary>
    Task<List<SyncResult>> SyncAllActiveAsync(CancellationToken ct);
}

public sealed record SyncResult(
    Guid     BindingId,
    string   ViewName,
    int      RowCount,
    string   JobId,
    TimeSpan Duration,
    string?  Error);
