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
    // GET /dm/reports/chi-phi-theo-khoa?sourceSystem=his-01&recordType=chung-tu&from=...&to=...
    [HttpGet("{reportCode}")]
    public async Task<IActionResult> GetReport(
        string reportCode,
        [FromQuery] string? sourceSystem,
        [FromQuery] string? recordType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetReportQuery(reportCode, sourceSystem, recordType, from, to), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<ReportDto>.Ok(result.Value))
            : NotFound(ApiResponse<ReportDto>.Fail(result.Error.Code, result.Error.Message));
    }
}
