using Hdos.Common.Responses;
using Hdos.LakehouseService.Application.Charts.Sdui;
using Hdos.LakehouseService.Infrastructure.Charts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.LakehouseService.API.Controllers;

/// <summary>
/// Chart trực tiếp query warehouse PG bằng raw SQL — KHÔNG đi qua StagingRecord /
/// SourceProfile mapping. Mỗi request = 1 query live.
///
/// Khác với <c>/dm/pages/{code}</c> (đọc canonical record đã sync), endpoint này:
///   • Realtime — không trễ theo chu kỳ sync
///   • Tận dụng SQL GROUP BY / WHERE / aggregate ở DB-side
///   • Không cần SourceProfile / không lưu storage
///
/// Phù hợp với KPI dashboard cần data luôn mới + filter động.
/// </summary>
[ApiController]
[Route("lakehouse/charts")]
[Tags("Lakehouse Charts — Direct query, không qua StagingRecord")]
public sealed class LakehouseChartsController(LakehouseChartBuilder builder) : ControllerBase
{
    /// <summary>List chart codes đã đăng ký.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<string>>), StatusCodes.Status200OK)]
    public IActionResult List() =>
        Ok(ApiResponse<IReadOnlyList<string>>.Ok(builder.AvailableCodes));

    /// <summary>
    /// Render 1 chart bằng raw SQL trực tiếp vào warehouse.
    /// Query params được pass nguyên xi sang chart config — mỗi config tự interpret.
    /// </summary>
    /// <param name="code">Chart code (lấy từ <c>GET /lakehouse/charts</c>).</param>
    /// <param name="date">(Optional) Ngày báo cáo yyyy-MM-dd. Mặc định hôm nay UTC.</param>
    [HttpGet("{code}")]
    [ProducesResponseType(typeof(ApiResponse<SduiPage>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        string code,
        [FromQuery] DateOnly? date,
        CancellationToken ct)
    {
        var page = await builder.BuildAsync(code, date, HttpContext.Request.Query, ct);
        if (page is null)
            return NotFound(ApiResponse<SduiPage>.Fail(
                "CHART.NOT_FOUND",
                $"Chart '{code}' chưa đăng ký. Xem danh sách: GET /lakehouse/charts"));

        return Ok(ApiResponse<SduiPage>.Ok(page));
    }
}
