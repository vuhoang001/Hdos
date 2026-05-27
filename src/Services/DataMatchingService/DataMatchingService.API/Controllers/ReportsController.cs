using Hdos.Common.Responses;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Application.Features.Reports;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DataMatchingService.API.Controllers;

[ApiController]
[Route("dm/reports")]
// [Authorize]
public sealed class ReportsController(ISender sender) : ControllerBase
{
    [HttpGet("{reportCode}")]
    public async Task<IActionResult> GetReport(
        string reportCode,
        [FromQuery] string? sourceSystem,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var query = new GetReportQuery(reportCode, sourceSystem, from, to);
        var result = await sender.Send(query, ct);

        return result.IsSuccess
            ? Ok(ApiResponse<ReportDto>.Ok(result.Value))
            : NotFound(ApiResponse<ReportDto>.Fail(result.Error.Code, result.Error.Message));
    }
}
