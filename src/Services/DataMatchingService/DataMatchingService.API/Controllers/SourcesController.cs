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
            ? CreatedAtAction(nameof(GetSources), null,
                ApiResponse<SourceProfileDto>.Ok(result.Value))
            : Conflict(ApiResponse<SourceProfileDto>.Fail(result.Error.Code, result.Error.Message));
    }

    [HttpGet]
    public async Task<IActionResult> GetSources(CancellationToken ct)
    {
        var result = await sender.Send(new GetSourcesQuery(), ct);
        return Ok(ApiResponse<List<SourceProfileDto>>.Ok(result.Value));
    }
}
