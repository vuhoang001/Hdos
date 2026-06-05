using Hdos.Common.Responses;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Application.Features.Records;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DataMatchingService.API.Controllers;

/// <summary>
/// Truy vấn các record đã được nạp và match. Đây là endpoint chính cho DynamicForm DataSource khi pre-fill form.
/// </summary>
[ApiController]
[Route("dm/records")]
[Tags("3. Records — Truy vấn dữ liệu đã match")]
// [Authorize]
public sealed class RecordsController(ISender sender) : ControllerBase
{
    /// <summary>Lấy 1 record theo ID (kèm canonical payload).</summary>
    /// <remarks>
    /// DynamicForm gọi endpoint này khi DataSource cấu hình
    /// <c>resourcePath: "/dm/records/{recordId}"</c> để pre-fill form theo expression
    /// <c>{{sources.record.TenBenhNhan}}</c>.
    /// </remarks>
    /// <param name="id">Guid của record trong staging.</param>
    /// <response code="200">Trả record + canonicalPayload.</response>
    /// <response code="404">Record không tồn tại.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<StagingRecordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRecord(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new GetRecordByIdQuery(id), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse<StagingRecordDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<StagingRecordDto>.Ok(result.Value));
    }

    /// <summary>Tìm kiếm record theo bộ lọc tự do (system / type / field / khoảng thời gian).</summary>
    /// <remarks>
    /// Tất cả query param đều optional, có thể kết hợp tự do.
    ///
    /// Ví dụ:
    /// <code>
    /// GET /dm/records?sourceSystem=his-01&amp;recordType=benh-nhan
    /// GET /dm/records?sourceSystem=his-01&amp;recordType=benh-nhan&amp;field=TenKhoa&amp;value=Tim+Mach
    /// GET /dm/records?sourceSystem=his-01&amp;recordType=chung-tu&amp;from=2026-01-01&amp;to=2026-03-31
    /// GET /dm/records?recordType=nhan-vien&amp;field=TenKhoa&amp;value=Noi+Tiet&amp;limit=50
    /// </code>
    /// </remarks>
    /// <param name="sourceSystem">(Optional) Lọc theo hệ thống nguồn.</param>
    /// <param name="recordType">(Optional) Lọc theo loại bản ghi.</param>
    /// <param name="field">(Optional) Tên field canonical để filter (dùng kèm <paramref name="value"/>).</param>
    /// <param name="value">(Optional) Giá trị tìm kiếm (substring) của <paramref name="field"/>.</param>
    /// <param name="from">(Optional) Lọc <c>IngestedAt &gt;= from</c>.</param>
    /// <param name="to">(Optional) Lọc <c>IngestedAt &lt;= to</c>.</param>
    /// <param name="limit">Số bản ghi tối đa (mặc định 200).</param>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<StagingRecordDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecords(
        [FromQuery] string? sourceSystem,
        [FromQuery] string? recordType,
        [FromQuery] string? field,
        [FromQuery] string? value,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        var query = new GetRecordsQuery(sourceSystem, recordType, field, value, from, to, limit);
        var result = await sender.Send(query, ct);
        return Ok(ApiResponse<List<StagingRecordDto>>.Ok(result.Value));
    }
}
