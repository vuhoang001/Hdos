using Hdos.Common.Responses;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Application.Features.Sources;
using Hdos.DataMatchingService.Application.Features.Sources.GetSchema;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DataMatchingService.API.Controllers;

[ApiController]
[Route("dm/sources")]
// [Authorize]
public sealed class SourcesController(ISender sender) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> RegisterSource(
        [FromBody] RegisterSourceCommand cmd,
        CancellationToken ct)
    {
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetSources), null, ApiResponse<SourceProfileDto>.Ok(result.Value))
            : Conflict(ApiResponse<SourceProfileDto>.Fail(result.Error.Code, result.Error.Message));
    }

    // GET /dm/sources                   → tất cả sources
    // GET /dm/sources?sourceSystem=his-01 → chỉ các loại của his-01
    [HttpGet]
    public async Task<IActionResult> GetSources(
        [FromQuery] string? sourceSystem,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetSourcesQuery(sourceSystem), ct);
        return Ok(ApiResponse<List<SourceProfileDto>>.Ok(result.Value));
    }

    // Schema Discovery — FE dùng để hiển thị dropdown field khi config DataBinding.
    // Trả danh sách canonical field có sẵn cho cặp (sourceSystem, recordType).
    [HttpGet("{sourceSystem}/{recordType}/schema")]
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
