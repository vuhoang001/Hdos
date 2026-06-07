using Hdos.Common.Responses;
using Hdos.LakehouseService.Application.DTOs;
using Hdos.LakehouseService.Application.Features.ViewBindings.CreateViewBinding;
using Hdos.LakehouseService.Application.Features.ViewBindings.CreateWithAutoProfile;
using Hdos.LakehouseService.Application.Features.ViewBindings.DeleteViewBinding;
using Hdos.LakehouseService.Application.Features.ViewBindings.ListViewBindings;
using Hdos.LakehouseService.Application.Features.ViewBindings.UpdateViewBinding;
using Hdos.LakehouseService.Infrastructure.Sync;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.LakehouseService.API.Controllers;

/// <summary>
/// CRUD đăng ký <c>ViewBinding</c> (PostgreSQL view → SourceSystem/RecordType) và trigger sync.
/// Mỗi binding khi sync sẽ publish <c>RawRecordIngestRequestedIntegrationEvent</c> vào
/// <c>DataMatchingService</c>. Xem doc 44 — Unified Ingest Pipeline §4.
/// </summary>
[ApiController]
[Route("lakehouse/view-bindings")]
[Tags("ViewBindings — Đăng ký nguồn lakehouse")]
public sealed class ViewBindingsController(
    ISender              sender,
    IWarehouseViewSyncer syncer,
    ISyncStateRepository syncState) : ControllerBase
{
    /// <summary>List tất cả ViewBinding. Lọc <c>?activeOnly=true</c> để chỉ lấy bindings đang active.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<ViewBindingDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool activeOnly = false, CancellationToken ct = default)
    {
        var result = await sender.Send(new ListViewBindingsQuery(activeOnly), ct);
        return Ok(ApiResponse<List<ViewBindingDto>>.Ok(result.Value));
    }

    /// <summary>
    /// MVP B — Tạo binding mới + tự động enroll SourceProfile sang DataMatching.
    /// Backend introspect view (information_schema.columns), sinh mapping convention
    /// (snake_case → PascalCase + domain overrides), gọi /dm/sources qua HTTP idempotent.
    /// Admin không cần gõ mappings tay. Xem doc 45 §7.
    /// </summary>
    /// <response code="201">Cả binding + SourceProfile đều sẵn sàng.</response>
    /// <response code="400">Validation fail / cột business-key hoặc updated-at không có trong view.</response>
    /// <response code="404">View không tồn tại / hdos_reader thiếu quyền SELECT.</response>
    /// <response code="409">Binding cho view này đã tồn tại.</response>
    /// <response code="502">DataMatchingService không reachable / enroll fail.</response>
    [HttpPost("with-auto-profile")]
    [ProducesResponseType(typeof(ApiResponse<CreateWithAutoProfileResultDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> CreateWithAutoProfile(
        [FromBody] CreateWithAutoProfileCommand cmd, CancellationToken ct)
    {
        var result = await sender.Send(cmd, ct);
        if (result.IsSuccess)
            return Created(string.Empty,
                ApiResponse<CreateWithAutoProfileResultDto>.Ok(result.Value));

        return result.Error.Code switch
        {
            "Conflict" => Conflict(ApiResponse.Fail(result.Error.Code, result.Error.Message)),
            "NotFound" => NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message)),
            "Validation" when result.Error.Message.Contains("DataMatching", StringComparison.OrdinalIgnoreCase)
                           || result.Error.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
                => StatusCode(StatusCodes.Status502BadGateway,
                              ApiResponse.Fail(result.Error.Code, result.Error.Message)),
            _ => BadRequest(ApiResponse.Fail(result.Error.Code, result.Error.Message))
        };
    }

    /// <summary>Tạo binding mới (manual — admin tự đăng ký SourceProfile trước qua /dm/sources).</summary>
    /// <response code="201">Tạo thành công.</response>
    /// <response code="409">Đã có binding khác trỏ tới cùng view.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ViewBindingDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateViewBindingCommand cmd, CancellationToken ct)
    {
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return Conflict(ApiResponse<ViewBindingDto>.Fail(result.Error.Code, result.Error.Message));
        return CreatedAtAction(nameof(List), null, ApiResponse<ViewBindingDto>.Ok(result.Value));
    }

    /// <summary>Cập nhật binding theo Id.</summary>
    /// <response code="404">Binding không tồn tại.</response>
    /// <response code="409">Đổi sang view name đã có binding khác chiếm.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ViewBindingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateViewBindingBody body, CancellationToken ct)
    {
        var cmd = new UpdateViewBindingCommand(
            id, body.ViewName, body.SourceSystem, body.RecordType,
            body.BusinessKeyColumn, body.UpdatedAtColumn, body.PollIntervalSeconds, body.IsActive);

        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return result.Error.Code == "NotFound"
                ? NotFound(ApiResponse<ViewBindingDto>.Fail(result.Error.Code, result.Error.Message))
                : Conflict(ApiResponse<ViewBindingDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<ViewBindingDto>.Ok(result.Value));
    }

    /// <summary>Xoá hẳn binding khỏi registry.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new DeleteViewBindingCommand(id), ct);
        return result.IsSuccess
            ? NoContent()
            : NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message));
    }

    /// <summary>Trigger sync ngay lập tức cho 1 binding cụ thể.</summary>
    /// <remarks>
    /// Đọc full data từ VIEW, publish mỗi row thành event. DataMatching dedup SHA-256
    /// xử lý trùng lặp. Trả về <c>jobId</c> để truy vết log.
    /// </remarks>
    [HttpPost("{id:guid}/sync")]
    [ProducesResponseType(typeof(ApiResponse<SyncResult>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Sync(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await syncer.SyncAsync(id, ct);
            return Accepted(ApiResponse<SyncResult>.Ok(result));
        }
        catch (ArgumentException ex)
        {
            return NotFound(ApiResponse.Fail("ViewBinding.NotFound", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("ViewBinding.Inactive", ex.Message));
        }
    }

    /// <summary>Trigger sync cho mọi binding đang active.</summary>
    /// <remarks>Lỗi 1 binding không stop các binding khác — kết quả trả về list.</remarks>
    [HttpPost("sync-all")]
    [ProducesResponseType(typeof(ApiResponse<List<SyncResult>>), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> SyncAll(CancellationToken ct)
    {
        var results = await syncer.SyncAllActiveAsync(ct);
        return Accepted(ApiResponse<List<SyncResult>>.Ok(results));
    }

    /// <summary>Trạng thái sync gần nhất của từng VIEW.</summary>
    [HttpGet("sync-status")]
    [ProducesResponseType(typeof(ApiResponse<List<SyncState>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncStatus(CancellationToken ct)
    {
        var list = await syncState.ListAsync(ct);
        return Ok(ApiResponse<List<SyncState>>.Ok(list));
    }
}

public sealed record UpdateViewBindingBody(
    string ViewName,
    string SourceSystem,
    string RecordType,
    string BusinessKeyColumn,
    string UpdatedAtColumn,
    int    PollIntervalSeconds,
    bool   IsActive);
