using Hdos.Common.Responses;
using Hdos.DataMatchingService.Application.Dashboard;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DataMatchingService.API.Controllers;

[ApiController]
[Route("dm/dashboards")]
// [Authorize]
public sealed class DashboardsController(DashboardEngine engine) : ControllerBase
{
    // GET /dm/dashboards
    // Trả về danh sách code của tất cả dashboard đã đăng ký.
    [HttpGet]
    public IActionResult List() =>
        Ok(ApiResponse<IReadOnlyList<string>>.Ok(engine.AvailableCodes));

    // GET /dm/dashboards/m02?sourceSystem=his-01&date=2026-05-28
    [HttpGet("{code}")]
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
