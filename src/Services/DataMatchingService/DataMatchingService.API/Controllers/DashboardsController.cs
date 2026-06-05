using Hdos.Common.Responses;
using Hdos.DataMatchingService.Application.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DataMatchingService.API.Controllers;

/// <summary>
/// Dashboard ngữ nghĩa cao — KPI / chart đã tổng hợp sẵn, phục vụ trang dashboard nghiệp vụ.
/// </summary>
[ApiController]
[Route("dm/dashboards")]
[Tags("5. Dashboards — KPI & chart")]
// [Authorize]
public sealed class DashboardsController(DashboardEngine engine) : ControllerBase
{
    /// <summary>Liệt kê code của tất cả dashboard đã đăng ký trong engine.</summary>
    /// <remarks>FE dùng để render menu dashboard động.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<string>>), StatusCodes.Status200OK)]
    public IActionResult List() =>
        Ok(ApiResponse<IReadOnlyList<string>>.Ok(engine.AvailableCodes));

    /// <summary>Lấy nội dung 1 dashboard theo mã + bộ lọc.</summary>
    /// <remarks>
    /// Ví dụ:
    /// <code>
    /// GET /dm/dashboards/m02?sourceSystem=his-01&amp;date=2026-05-28
    /// </code>
    /// </remarks>
    /// <param name="code">Mã dashboard (lấy từ <c>GET /dm/dashboards</c>).</param>
    /// <param name="sourceSystem">(Optional) Lọc theo hệ thống nguồn.</param>
    /// <param name="date">(Optional) Ngày tham chiếu (yyyy-MM-dd). Mặc định = hôm nay nếu dashboard cần.</param>
    /// <response code="200">Trả KPI + chart data.</response>
    /// <response code="404">Dashboard code không tồn tại.</response>
    [HttpGet("{code}")]
    [ProducesResponseType(typeof(ApiResponse<DashboardResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        string code,
        [FromQuery] string? sourceSystem,
        [FromQuery] DateOnly? date,
        CancellationToken ct)
    {
        var result = await engine.ExecuteAsync(code, sourceSystem, date, ct);
        return result is null
            ? NotFound(ApiResponse<DashboardResult>.Fail(
                "DASHBOARD.NOT_FOUND", $"Dashboard '{code}' không tồn tại. Dùng GET /dm/dashboards để xem danh sách."))
            : Ok(ApiResponse<DashboardResult>.Ok(result));
    }
}
