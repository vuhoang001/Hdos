using Hdos.Common.Responses;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Screens.CreateScreen;
using Hdos.DynamicFormService.Application.Features.Screens.DeleteScreen;
using Hdos.DynamicFormService.Application.Features.Screens.GetScreens;
using Hdos.DynamicFormService.Application.Features.Screens.PublishScreen;
using Hdos.DynamicFormService.Application.Features.Screens.UpdateScreen;
using Hdos.DynamicFormService.Application.Features.Tabs.CreateTab;
using Hdos.DynamicFormService.Application.Features.Tabs.DeleteTab;
using Hdos.DynamicFormService.Application.Features.Screens.SetDataSources;
using Hdos.DynamicFormService.Application.Features.Tabs.SaveTabWidgets;
using Hdos.DynamicFormService.Application.Features.Tabs.UpdateTab;
using Hdos.DynamicFormService.Application.Features.WidgetCatalog.GetWidgetCatalog;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DynamicFormService.API.Controllers;

[ApiController]
[Route("forms/admin/screens")]
// [Authorize(Policy = HdosPermissions.FormsAdmin)]
public sealed class AdminScreensController(ISender sender) : ControllerBase
{
    // ── Widget Catalog ────────────────────────────────────────────────────────

    [HttpGet("/forms/admin/widget-catalog")]
    public async Task<IActionResult> GetWidgetCatalog([FromQuery] string? category, CancellationToken ct)
    {
        var result = await sender.Send(new GetWidgetCatalogQuery(category), ct);
        return Ok(ApiResponse<List<WidgetCatalogItemDto>>.Ok(result.Value));
    }

    // ── Screens ───────────────────────────────────────────────────────────────

    [HttpGet("{moduleCode}")]
    public async Task<IActionResult> GetScreens(string moduleCode, CancellationToken ct)
    {
        var result = await sender.Send(new GetScreensQuery(moduleCode), ct);
        return Ok(ApiResponse<List<FormScreenDto>>.Ok(result.Value));
    }

    [HttpPost]
    public async Task<IActionResult> CreateScreen([FromBody] CreateScreenBody body, CancellationToken ct)
    {
        var cmd    = new CreateScreenCommand(body.ModuleCode, body.Code, body.Title, body.Description, body.SortOrder);
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<FormScreenDto>.Fail(result.Error.Code, result.Error.Message));
        return CreatedAtAction(null, ApiResponse<FormScreenDto>.Ok(result.Value));
    }

    [HttpPut("{moduleCode}/{screenCode}")]
    public async Task<IActionResult> UpdateScreen(
        string                 moduleCode,
        string                 screenCode,
        [FromBody] UpdateScreenBody body,
        CancellationToken      ct)
    {
        var cmd    = new UpdateScreenCommand(moduleCode, screenCode, body.Title, body.Description, body.SortOrder);
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<FormScreenDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<FormScreenDto>.Ok(result.Value));
    }

    [HttpDelete("{moduleCode}/{screenCode}")]
    public async Task<IActionResult> DeleteScreen(string moduleCode, string screenCode, CancellationToken ct)
    {
        var result = await sender.Send(new DeleteScreenCommand(moduleCode, screenCode), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return NoContent();
    }

    [HttpPost("{moduleCode}/{screenCode}/publish")]
    public async Task<IActionResult> PublishScreen(string moduleCode, string screenCode, CancellationToken ct)
    {
        var result = await sender.Send(new PublishScreenCommand(moduleCode, screenCode), ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse.Ok());
    }

    // ── Data Sources (Expression Binding) ────────────────────────────────────

    /// <summary>
    /// Khai báo danh sách data sources cho screen — frontend dùng để biết fetch data từ đâu.
    /// Gọi lại để thay thế toàn bộ danh sách. Gửi mảng rỗng để xóa hết.
    /// </summary>
    [HttpPut("{moduleCode}/{screenCode}/data-sources")]
    public async Task<IActionResult> SetDataSources(
        string                       moduleCode,
        string                       screenCode,
        [FromBody] List<DataSourceInput> body,
        CancellationToken            ct)
    {
        var cmd    = new SetScreenDataSourcesCommand(moduleCode, screenCode, body);
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse.Ok());
    }

    // ── Tabs ──────────────────────────────────────────────────────────────────

    [HttpPost("{moduleCode}/{screenCode}/tabs")]
    public async Task<IActionResult> CreateTab(
        string               moduleCode,
        string               screenCode,
        [FromBody] CreateTabBody body,
        CancellationToken    ct)
    {
        var cmd    = new CreateTabCommand(moduleCode, screenCode, body.Label, body.Slug, body.SortOrder, body.IsDefault);
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<FormScreenTabDto>.Fail(result.Error.Code, result.Error.Message));
        return CreatedAtAction(null, ApiResponse<FormScreenTabDto>.Ok(result.Value));
    }

    [HttpPut("{moduleCode}/{screenCode}/tabs/{tabId:guid}")]
    public async Task<IActionResult> UpdateTab(
        string               moduleCode,
        string               screenCode,
        Guid                 tabId,
        [FromBody] UpdateTabBody body,
        CancellationToken    ct)
    {
        var cmd    = new UpdateTabCommand(moduleCode, screenCode, tabId, body.Label, body.SortOrder, body.IsDefault);
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<FormScreenTabDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<FormScreenTabDto>.Ok(result.Value));
    }

    [HttpDelete("{moduleCode}/{screenCode}/tabs/{tabId:guid}")]
    public async Task<IActionResult> DeleteTab(
        string            moduleCode,
        string            screenCode,
        Guid              tabId,
        CancellationToken ct)
    {
        var result = await sender.Send(new DeleteTabCommand(moduleCode, screenCode, tabId), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return NoContent();
    }

    // ── Widgets (drag-and-drop save) ──────────────────────────────────────────

    /// <summary>
    /// Lưu toàn bộ widget layout cho một tab — full replacement.
    /// Gọi khi user nhấn "Lưu" sau khi kéo thả widget trong designer.
    /// </summary>
    [HttpPut("{moduleCode}/{screenCode}/tabs/{tabId:guid}/widgets")]
    public async Task<IActionResult> SaveWidgets(
        string                       moduleCode,
        string                       screenCode,
        Guid                         tabId,
        [FromBody] List<WidgetInput>  widgets,
        CancellationToken            ct)
    {
        var cmd    = new SaveTabWidgetsCommand(moduleCode, screenCode, tabId, widgets);
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<object>.Ok(new { saved = result.Value }));
    }
}

public sealed record CreateScreenBody(
    string  ModuleCode,
    string  Code,
    string  Title,
    string? Description,
    int     SortOrder = 0);

public sealed record UpdateScreenBody(
    string  Title,
    string? Description,
    int     SortOrder);

public sealed record CreateTabBody(
    string Label,
    string Slug,
    int    SortOrder = 0,
    bool   IsDefault = false);

public sealed record UpdateTabBody(
    string Label,
    int    SortOrder,
    bool   IsDefault);
