using Hdos.Common.Responses;
using Hdos.LakehouseService.Infrastructure.Sync;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.LakehouseService.API.Controllers;

/// <summary>
/// Trigger pull data từ warehouse external vào LakehouseService (manual).
/// Demo Phase 1: chỉ support view <c>encounter_activity_daily</c>.
/// </summary>
[ApiController]
[Route("lakehouse/admin/sync")]
[Tags("Admin — Warehouse Sync")]
public sealed class AdminSyncController(
    IWarehouseViewSyncer syncer,
    ISyncStateRepository syncState) : ControllerBase
{
    /// <summary>Trigger pull 1 view → publish events → consumer upsert vào LakehouseSnapshots.</summary>
    /// <remarks>
    /// Ví dụ:
    /// <code>
    /// POST /lakehouse/admin/sync/encounter_activity_daily
    /// </code>
    /// Response trả về số row đã publish + jobId truy vết.
    /// </remarks>
    /// <param name="viewName">Tên VIEW (không kèm schema). VD: <c>encounter_activity_daily</c>.</param>
    /// <response code="202">Đã pull + publish. Consumer xử lý async, kết quả xuất hiện trong /lakehouse/snapshots sau vài giây.</response>
    /// <response code="400">View chưa được khai báo trong syncer.</response>
    /// <response code="503">Warehouse sync chưa cấu hình (thiếu ConnectionStrings__Warehouse).</response>
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
}
