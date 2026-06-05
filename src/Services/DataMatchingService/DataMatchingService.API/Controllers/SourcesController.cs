using Hdos.Common.Responses;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Application.Features.Sources;
using Hdos.DataMatchingService.Application.Features.Sources.GetSchema;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DataMatchingService.API.Controllers;

/// <summary>
/// Quản lý SourceProfile — ánh xạ field raw (HIS / BHYT) → canonical schema. Phải đăng ký trước khi ingest.
/// </summary>
[ApiController]
[Route("dm/sources")]
[Tags("2. Source Profile & Schema")]
// [Authorize]
public sealed class SourcesController(ISender sender) : ControllerBase
{
    /// <summary>Đăng ký SourceProfile mới (mapping raw → canonical).</summary>
    /// <remarks>
    /// Khai báo 1 lần cho mỗi cặp (<c>sourceSystem</c>, <c>recordType</c>).
    ///
    /// Ví dụ body:
    /// <code>
    /// {
    ///   "sourceSystem":      "his-01",
    ///   "recordType":        "benh-nhan",
    ///   "displayName":       "Bệnh nhân nội trú (HIS-01)",
    ///   "businessKeyField":  "MaBenhNhan",
    ///   "mappings": {
    ///     "ho_ten":       "TenBenhNhan",
    ///     "ma_benh_nhan": "MaBenhNhan"
    ///   }
    /// }
    /// </code>
    /// </remarks>
    /// <response code="201">Đăng ký thành công.</response>
    /// <response code="409">Cặp (sourceSystem, recordType) đã tồn tại.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<SourceProfileDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterSource(
        [FromBody] RegisterSourceCommand cmd,
        CancellationToken ct)
    {
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetSources), null, ApiResponse<SourceProfileDto>.Ok(result.Value))
            : Conflict(ApiResponse<SourceProfileDto>.Fail(result.Error.Code, result.Error.Message));
    }

    /// <summary>Danh sách SourceProfile đã đăng ký.</summary>
    /// <remarks>
    /// Không truyền query → trả tất cả.
    /// Truyền <c>?sourceSystem=his-01</c> → chỉ trả profile của hệ thống đó.
    /// </remarks>
    /// <param name="sourceSystem">(Optional) Lọc theo hệ thống nguồn.</param>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<SourceProfileDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSources(
        [FromQuery] string? sourceSystem,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetSourcesQuery(sourceSystem), ct);
        return Ok(ApiResponse<List<SourceProfileDto>>.Ok(result.Value));
    }

    /// <summary>Schema Discovery — danh sách canonical field của 1 SourceProfile.</summary>
    /// <remarks>
    /// Dùng cho admin UI khi config DataBinding ở DynamicForm: hiện dropdown các field FE có thể bind
    /// thay vì gõ tay <c>{{sources.x.fieldName}}</c>.
    /// </remarks>
    /// <param name="sourceSystem">Mã hệ thống nguồn. VD: <c>his-01</c>.</param>
    /// <param name="recordType">Loại bản ghi. VD: <c>benh-nhan</c>.</param>
    /// <response code="200">Trả schema (danh sách field + type).</response>
    /// <response code="404">SourceProfile không tồn tại.</response>
    [HttpGet("{sourceSystem}/{recordType}/schema")]
    [ProducesResponseType(typeof(ApiResponse<DataSourceSchemaDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSchema(
        string sourceSystem,
        string recordType,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetSourceSchemaQuery(sourceSystem, recordType), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<DataSourceSchemaDto>.Ok(result.Value))
            : NotFound(ApiResponse<DataSourceSchemaDto>.Fail(result.Error.Code, result.Error.Message));
    }
}
