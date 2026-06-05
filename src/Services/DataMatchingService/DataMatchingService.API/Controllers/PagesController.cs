using Hdos.Common.Responses;
using Hdos.DataMatchingService.Application.Sdui;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DataMatchingService.API.Controllers;

/// <summary>
/// SDUI page — server-driven UI: BE trả layout JSON, FE render mà không cần code mới.
/// </summary>
[ApiController]
[Route("dm/pages")]
[Tags("6. SDUI Pages — Server-driven UI")]
public sealed class PagesController(SduiEngine engine) : ControllerBase
{
    /// <summary>Liệt kê code của tất cả SDUI page đã đăng ký.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<string>>), StatusCodes.Status200OK)]
    public IActionResult List() =>
        Ok(ApiResponse<IReadOnlyList<string>>.Ok(engine.AvailableCodes));

    /// <summary>Render layout 1 SDUI page theo mã + bộ lọc.</summary>
    /// <remarks>
    /// Ví dụ:
    /// <code>
    /// GET /dm/pages/{code}?sourceSystem=his-01&amp;date=2026-05-28
    /// </code>
    /// FE đọc cấu trúc tabs/widgets/data trả về và render trực tiếp.
    /// </remarks>
    /// <param name="code">Mã page (lấy từ <c>GET /dm/pages</c>).</param>
    /// <param name="sourceSystem">(Optional) Lọc theo hệ thống nguồn.</param>
    /// <param name="date">(Optional) Ngày tham chiếu (yyyy-MM-dd).</param>
    /// <response code="200">Trả layout SDUI hoàn chỉnh.</response>
    /// <response code="404">Page code không tồn tại.</response>
    [HttpGet("{code}")]
    [ProducesResponseType(typeof(ApiResponse<SduiPage>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
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
