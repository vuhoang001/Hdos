using Hdos.Common.Responses;
using Hdos.LakehouseService.Infrastructure.Sync;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.LakehouseService.API.Controllers;

/// <summary>
/// Trigger pull data từ warehouse external (Postgres serving `api.*`) vào LakehouseService (manual).
/// Hỗ trợ 6 view: encounter_activity_daily, finance_daily, inpatient_summary_daily,
/// bed_occupancy, clinical_pathway, medicine_stock.
///
/// Trigger thay thế: DE publish <c>WarehouseRefreshedIntegrationEvent</c> qua RabbitMQ —
/// <c>WarehouseRefreshedConsumer</c> tự sync, không cần REST call.
/// </summary>
[ApiController]
[Route("lakehouse/admin/sync")]
[Tags("Admin — Warehouse Sync")]
public sealed class AdminSyncController(
    IWarehouseViewSyncer syncer,
    ISyncStateRepository syncState) : ControllerBase
{
    /// <summary>Danh sách view mà syncer hỗ trợ.</summary>
    /// <remarks>FE/admin dùng để hiển thị dropdown khi muốn trigger sync.</remarks>
    [HttpGet("views")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyCollection<string>>), StatusCodes.Status200OK)]
    public IActionResult Views() =>
        Ok(ApiResponse<IReadOnlyCollection<string>>.Ok(syncer.SupportedViewNames));

    /// <summary>Trạng thái sync gần nhất của từng VIEW.</summary>
    /// <remarks>
    /// Trả danh sách <c>{viewName, lastSyncedAt, lastRowCount, lastJobId}</c> cho mọi view đã sync ít nhất 1 lần.
    /// </remarks>
    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiResponse<List<SyncState>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var list = await syncState.ListAsync(ct);
        return Ok(ApiResponse<List<SyncState>>.Ok(list));
    }

    /// <summary>Trigger pull TẤT CẢ view → publish events → consumer upsert vào LakehouseSnapshots.</summary>
    /// <remarks>
    /// Lỗi 1 view không stop các view còn lại — mỗi view sync độc lập.
    /// Trả về danh sách kết quả cho từng view.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<List<SyncResult>>), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> SyncAll(CancellationToken ct)
    {
        var results = new List<SyncResult>();
        foreach (var viewName in syncer.SupportedViewNames)
        {
            try { results.Add(await syncer.SyncAsync(viewName, ct)); }
            catch (Exception ex)
            {
                results.Add(new SyncResult(viewName, -1, $"FAILED: {ex.Message}", TimeSpan.Zero));
            }
        }
        return Accepted(ApiResponse<List<SyncResult>>.Ok(results));
    }

    /// <summary>Trigger pull 1 view → publish events → consumer upsert vào LakehouseSnapshots.</summary>
    /// <remarks>
    /// Ví dụ:
    /// <code>
    /// POST /lakehouse/admin/sync/encounter_activity_daily
    /// </code>
    /// Response trả về số row đã publish + jobId truy vết.
    /// </remarks>
    /// <param name="viewName">Tên VIEW (không kèm schema). Xem danh sách qua <c>GET /lakehouse/admin/sync/views</c>.</param>
    /// <response code="202">Đã pull + publish. Consumer xử lý async, kết quả xuất hiện trong /lakehouse/snapshots sau vài giây.</response>
    /// <response code="400">View chưa được khai báo trong syncer.</response>
    [HttpPost("{viewName}")]
    [ProducesResponseType(typeof(ApiResponse<SyncResult>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Sync(string viewName, CancellationToken ct)
    {
        try
        {
            var result = await syncer.SyncAsync(viewName, ct);
            return Accepted(ApiResponse<SyncResult>.Ok(result));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse.Fail("SYNC.UNKNOWN_VIEW", ex.Message));
        }
    }
}
