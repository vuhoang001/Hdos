using Hdos.Common.Responses;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Screens.GetScreenLayout;
using Hdos.DynamicFormService.Application.Features.Screens.GetScreens;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DynamicFormService.API.Controllers;

[ApiController]
[Route("forms/screens")]
public sealed class ScreensController(ISender sender) : ControllerBase
{
    /// <summary>Danh sách screens của module (chỉ published).</summary>
    [AllowAnonymous]
    [HttpGet("{moduleCode}")]
    public async Task<IActionResult> GetScreens(string moduleCode, CancellationToken ct)
    {
        var result = await sender.Send(new GetScreensQuery(moduleCode), ct);
        return Ok(ApiResponse<List<FormScreenDto>>.Ok(result.Value));
    }

    /// <summary>
    /// SDUI endpoint chính — trả toàn bộ layout (tabs + widgets) của một screen.
    /// Frontend dùng để render màn hình mà không cần biết cấu trúc trước.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{moduleCode}/{screenCode}/layout")]
    public async Task<IActionResult> GetScreenLayout(
        string            moduleCode,
        string            screenCode,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetScreenLayoutQuery(moduleCode, screenCode), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse<ScreenLayoutDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<ScreenLayoutDto>.Ok(result.Value));
    }
}
