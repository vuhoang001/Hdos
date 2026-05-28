using Hdos.Common.Responses;
using Hdos.DataMatchingService.Application.Sdui;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DataMatchingService.API.Controllers;

[ApiController]
[Route("dm/pages")]
public sealed class PagesController(SduiEngine engine) : ControllerBase
{
    /// <summary>GET /dm/pages — danh sách page codes khả dụng</summary>
    [HttpGet]
    public IActionResult List() =>
        Ok(ApiResponse<IReadOnlyList<string>>.Ok(engine.AvailableCodes));

    /// <summary>GET /dm/pages/{code}?sourceSystem=&amp;date=yyyy-MM-dd — render SDUI page</summary>
    [HttpGet("{code}")]
    public async Task<IActionResult> Get(
        string code,
        [FromQuery] string? sourceSystem,
        [FromQuery] DateOnly? date,
        CancellationToken ct)
    {
        var page = await engine.ExecuteAsync(code, sourceSystem, date, ct);
        if (page is null)
            return NotFound(ApiResponse<SduiPage>.Fail(
                "PAGE.NOT_FOUND", $"Page '{code}' không tồn tại. Dùng GET /dm/pages để xem danh sách."));

        return Ok(ApiResponse<SduiPage>.Ok(page));
    }
}
