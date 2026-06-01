using Hdos.Common.Responses;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Forms.AddField;
using Hdos.DynamicFormService.Application.Features.Forms.ArchiveForm;
using Hdos.DynamicFormService.Application.Features.Forms.CreateForm;
using Hdos.DynamicFormService.Application.Features.Forms.PublishForm;
using Hdos.DynamicFormService.Application.Features.Modules.CreateModule;
using Hdos.DynamicFormService.Application.Features.Pages.CreatePage;
using Hdos.DynamicFormService.Application.Features.Pages.PublishPage;
using Hdos.DynamicFormService.Application.Features.Pages.UpdatePageLayout;
using Hdos.DynamicFormService.Application.Features.Submissions.GetSubmissions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DynamicFormService.API.Controllers;

[ApiController]
[Route("forms/admin")]
// [Authorize(Policy = HdosPermissions.FormsAdmin)]
public sealed class AdminFormsController(ISender sender) : ControllerBase
{
    // ── Modules ───────────────────────────────────────────────────────────────

    [HttpPost("modules")]
    public async Task<IActionResult> CreateModule([FromBody] CreateModuleCommand cmd, CancellationToken ct)
    {
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<FormModuleDto>.Fail(result.Error.Code, result.Error.Message));
        return CreatedAtAction(null, ApiResponse<FormModuleDto>.Ok(result.Value));
    }

    // ── Forms ─────────────────────────────────────────────────────────────────

    [HttpPost("modules/{moduleCode}/forms")]
    public async Task<IActionResult> CreateForm(
        string                 moduleCode,
        [FromBody] CreateFormBody body,
        CancellationToken      ct)
    {
        var cmd = new CreateFormCommand(
            moduleCode, body.Key, body.Name, body.Description,
            body.SubmitButtonLabel, body.SuccessMessage, body.AllowMultipleSubmissions);
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<FormTemplateDto>.Fail(result.Error.Code, result.Error.Message));
        return CreatedAtAction(null, ApiResponse<FormTemplateDto>.Ok(result.Value));
    }

    [HttpPost("forms/{formTemplateId:guid}/fields")]
    public async Task<IActionResult> AddField(
        Guid                  formTemplateId,
        [FromBody] AddFieldCommand cmd,
        CancellationToken     ct)
    {
        var result = await sender.Send(cmd with { FormTemplateId = formTemplateId }, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<FormFieldDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<FormFieldDto>.Ok(result.Value));
    }

    [HttpPost("forms/{formTemplateId:guid}/publish")]
    public async Task<IActionResult> Publish(Guid formTemplateId, CancellationToken ct)
    {
        var result = await sender.Send(new PublishFormCommand(formTemplateId), ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse.Ok());
    }

    [HttpPost("forms/{formTemplateId:guid}/archive")]
    public async Task<IActionResult> Archive(Guid formTemplateId, CancellationToken ct)
    {
        var result = await sender.Send(new ArchiveFormCommand(formTemplateId), ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse.Ok());
    }

    // ── Pages ─────────────────────────────────────────────────────────────────

    [HttpPost("modules/{moduleCode}/pages")]
    public async Task<IActionResult> CreatePage(
        string                    moduleCode,
        [FromBody] CreatePageBody body,
        CancellationToken         ct)
    {
        var cmd = new CreatePageCommand(moduleCode, body.Code, body.Title, body.Description);
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<FormPageDto>.Fail(result.Error.Code, result.Error.Message));
        return CreatedAtAction(null, ApiResponse<FormPageDto>.Ok(result.Value));
    }

    [HttpPut("pages/{pageId:guid}/layout")]
    public async Task<IActionResult> UpdatePageLayout(
        Guid                         pageId,
        [FromBody] FormPageLayout     layout,
        CancellationToken            ct)
    {
        var result = await sender.Send(new UpdatePageLayoutCommand(pageId, layout), ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse.Ok());
    }

    [HttpPost("pages/{pageId:guid}/publish")]
    public async Task<IActionResult> PublishPage(Guid pageId, CancellationToken ct)
    {
        var result = await sender.Send(new PublishPageCommand(pageId), ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse.Ok());
    }

    // ── Submissions ───────────────────────────────────────────────────────────

    [HttpGet("forms/{formTemplateId:guid}/submissions")]
    public async Task<IActionResult> GetSubmissions(
        Guid formTemplateId,
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new GetSubmissionsQuery(formTemplateId, page, pageSize), ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<List<FormSubmissionDto>>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<List<FormSubmissionDto>>.Ok(result.Value));
    }
}

public sealed record CreateFormBody(
    string  Key,
    string  Name,
    string? Description,
    string  SubmitButtonLabel        = "Gửi",
    string  SuccessMessage           = "Đã gửi form thành công",
    bool    AllowMultipleSubmissions  = true);

public sealed record CreatePageBody(
    string  Code,
    string  Title,
    string? Description);
