using Hdos.Common.Responses;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Application.Features.Sources;
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
}
