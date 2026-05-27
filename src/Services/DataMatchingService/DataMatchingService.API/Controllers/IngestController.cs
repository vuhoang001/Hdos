using System.Text.Json;
using Hdos.Common.Responses;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Application.Features.Ingest;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DataMatchingService.API.Controllers;

[ApiController]
[Route("dm/ingest")]
// [Authorize]
public sealed class IngestController(ISender sender) : ControllerBase
{
    [HttpPost("json")]
    public async Task<IActionResult> IngestJson(
        [FromBody] IngestJsonRequest body,
        CancellationToken ct)
    {
        var rawPayload = body.Payload.GetRawText();
        var cmd = new IngestJsonCommand(body.SourceSystem, rawPayload, body.BusinessKeyOverride);
        var result = await sender.Send(cmd, ct);

        return result.IsSuccess
            ? Accepted(ApiResponse<IngestResultDto>.Ok(result.Value))
            : result.Error.Code == "Conflict"
                ? Conflict(ApiResponse<IngestResultDto>.Fail(result.Error.Code, result.Error.Message))
                : NotFound(ApiResponse<IngestResultDto>.Fail(result.Error.Code, result.Error.Message));
    }

    [HttpPost("file")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB limit
    public async Task<IActionResult> IngestFile(
        IFormFile file,
        [FromForm] string sourceSystem,
        [FromForm] string? businessKeyOverride,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("BadRequest", "No file uploaded."));

        using var stream = file.OpenReadStream();
        var cmd = new IngestFileCommand(sourceSystem, stream, file.FileName, businessKeyOverride);
        var result = await sender.Send(cmd, ct);

        return result.IsSuccess
            ? Accepted(ApiResponse<IngestBatchResultDto>.Ok(result.Value))
            : NotFound(ApiResponse<IngestBatchResultDto>.Fail(result.Error.Code, result.Error.Message));
    }
}

public sealed record IngestJsonRequest(
    string SourceSystem,
    JsonElement Payload,
    string? BusinessKeyOverride);
