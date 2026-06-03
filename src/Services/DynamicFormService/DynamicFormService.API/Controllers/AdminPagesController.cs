using Hdos.Common.Responses;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Screens.CreateScreen;
using Hdos.DynamicFormService.Application.Features.Screens.DeleteScreen;
using Hdos.DynamicFormService.Application.Features.Screens.GetScreens;
using Hdos.DynamicFormService.Application.Features.Screens.PublishScreen;
using Hdos.DynamicFormService.Application.Features.Screens.UpdateScreen;
using Hdos.DynamicFormService.Application.Features.Tabs.CreateTab;
using Hdos.DynamicFormService.Application.Features.Tabs.DeleteTab;
using Hdos.DynamicFormService.Application.Features.Tabs.SaveTabWidgets;
using Hdos.DynamicFormService.Application.Features.Tabs.UpdateTab;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DynamicFormService.API.Controllers;

/// <summary>
/// Admin API cho pages — URL convention nhất quán với public GET /forms/{moduleCode}/pages.
/// Cùng data với AdminScreensController nhưng dùng "pages" thay vì "screens".
/// </summary>
[ApiController]
[Route("forms/admin/{moduleCode}/pages")]
// [Authorize(Policy = HdosPermissions.FormsAdmin)]
public sealed class AdminPagesController(ISender sender) : ControllerBase
{
    // ── Pages (Screens) ───────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetPages(string moduleCode, CancellationToken ct)
    {
        var result = await sender.Send(new GetScreensQuery(moduleCode), ct);
        return Ok(ApiResponse<List<FormScreenDto>>.Ok(result.Value));
    }

    [HttpPost]
    public async Task<IActionResult> CreatePage(
        string moduleCode,
        [FromBody] CreatePageBody body,
        CancellationToken ct)
    {
        var cmd    = new CreateScreenCommand(moduleCode, body.Code, body.Title, body.Description, body.SortOrder);
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<FormScreenDto>.Fail(result.Error.Code, result.Error.Message));
        return CreatedAtAction(null, ApiResponse<FormScreenDto>.Ok(result.Value));
    }

    [HttpPut("{pageCode}")]
    public async Task<IActionResult> UpdatePage(
        string                  moduleCode,
        string                  pageCode,
        [FromBody] UpdatePageBody body,
        CancellationToken        ct)
    {
        var cmd    = new UpdateScreenCommand(moduleCode, pageCode, body.Title, body.Description, body.SortOrder);
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<FormScreenDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<FormScreenDto>.Ok(result.Value));
    }

    [HttpDelete("{pageCode}")]
    public async Task<IActionResult> DeletePage(string moduleCode, string pageCode, CancellationToken ct)
    {
        var result = await sender.Send(new DeleteScreenCommand(moduleCode, pageCode), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return NoContent();
    }

    [HttpPost("{pageCode}/publish")]
    public async Task<IActionResult> PublishPage(string moduleCode, string pageCode, CancellationToken ct)
    {
        var result = await sender.Send(new PublishScreenCommand(moduleCode, pageCode), ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse.Ok());
    }

    // ── Tabs ──────────────────────────────────────────────────────────────────

    [HttpPost("{pageCode}/tabs")]
    public async Task<IActionResult> CreateTab(
        string                moduleCode,
        string                pageCode,
        [FromBody] CreateTabBody body,
        CancellationToken     ct)
    {
        var cmd    = new CreateTabCommand(moduleCode, pageCode, body.Label, body.Slug, body.SortOrder, body.IsDefault);
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<FormScreenTabDto>.Fail(result.Error.Code, result.Error.Message));
        return CreatedAtAction(null, ApiResponse<FormScreenTabDto>.Ok(result.Value));
    }

    [HttpPut("{pageCode}/tabs/{tabId:guid}")]
    public async Task<IActionResult> UpdateTab(
        string                moduleCode,
        string                pageCode,
        Guid                  tabId,
        [FromBody] UpdateTabBody body,
        CancellationToken     ct)
    {
        var cmd    = new UpdateTabCommand(moduleCode, pageCode, tabId, body.Label, body.SortOrder, body.IsDefault);
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<FormScreenTabDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<FormScreenTabDto>.Ok(result.Value));
    }

    [HttpDelete("{pageCode}/tabs/{tabId:guid}")]
    public async Task<IActionResult> DeleteTab(
        string            moduleCode,
        string            pageCode,
        Guid              tabId,
        CancellationToken ct)
    {
        var result = await sender.Send(new DeleteTabCommand(moduleCode, pageCode, tabId), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return NoContent();
    }

    // ── Widgets (drag-and-drop save) ──────────────────────────────────────────

    [HttpPut("{pageCode}/tabs/{tabId:guid}/widgets")]
    public async Task<IActionResult> SaveWidgets(
        string                       moduleCode,
        string                       pageCode,
        Guid                         tabId,
        [FromBody] List<WidgetInput>  widgets,
        CancellationToken            ct)
    {
        var cmd    = new SaveTabWidgetsCommand(moduleCode, pageCode, tabId, widgets);
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<object>.Ok(new { saved = result.Value }));
    }
}

public sealed record CreatePageBody(
    string  Code,
    string  Title,
    string? Description,
    int     SortOrder = 0);

public sealed record UpdatePageBody(
    string  Title,
    string? Description,
    int     SortOrder);
