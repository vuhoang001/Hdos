using Hdos.Common.Responses;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Application.Features.Reports;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DataMatchingService.API.Controllers;

/// <summary>
/// Báo cáo dạng tổng hợp (aggregation) — group/sum/count trên record đã match.
/// </summary>
[ApiController]
[Route("dm/reports")]
[Tags("4. Reports — Báo cáo tổng hợp")]
// [Authorize]
public sealed class ReportsController(ISender sender) : ControllerBase
{
    /// <summary>Chạy 1 báo cáo theo mã (reportCode) + bộ lọc.</summary>
    /// <remarks>
    /// Mỗi <paramref name="reportCode"/> là 1 báo cáo định nghĩa sẵn trong Application layer
    /// (VD: <c>chi-phi-theo-khoa</c>, <c>so-luong-benh-nhan-theo-ngay</c>).
    ///
    /// Ví dụ:
    /// <code>
    /// GET /dm/reports/chi-phi-theo-khoa?sourceSystem=his-01&amp;recordType=chung-tu&amp;from=2026-01-01&amp;to=2026-03-31
    /// </code>
    /// </remarks>
    /// <param name="reportCode">Mã báo cáo định nghĩa trong code.</param>
    /// <param name="sourceSystem">(Optional) Lọc theo hệ thống nguồn.</param>
    /// <param name="recordType">(Optional) Lọc theo loại bản ghi.</param>
    /// <param name="from">(Optional) Khoảng thời gian bắt đầu.</param>
    /// <param name="to">(Optional) Khoảng thời gian kết thúc.</param>
    /// <response code="200">Trả dữ liệu báo cáo (rows + meta).</response>
    /// <response code="404">Báo cáo không tồn tại.</response>
    [HttpGet("{reportCode}")]
    [ProducesResponseType(typeof(ApiResponse<ReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
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
