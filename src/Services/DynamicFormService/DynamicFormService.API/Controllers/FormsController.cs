using Hdos.Common.Responses;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Forms.GetForms;
using Hdos.DynamicFormService.Application.Features.Forms.GetSchema;
using Hdos.DynamicFormService.Application.Features.Modules.GetModules;
using Hdos.DynamicFormService.Application.Features.Screens.GetPublishedScreensByModule;
using Hdos.DynamicFormService.Application.Features.Submissions.SubmitForm;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DynamicFormService.API.Controllers;

[ApiController]
[Route("forms")]
public sealed class FormsController(ISender sender) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("modules")]
    public async Task<IActionResult> GetModules(CancellationToken ct)
    {
        var result = await sender.Send(new GetModulesQuery(), ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<List<FormModuleDto>>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<List<FormModuleDto>>.Ok(result.Value));
    }

    [AllowAnonymous]
    [HttpGet("{moduleCode}/pages")]
    public async Task<IActionResult> GetPages(string moduleCode, CancellationToken ct)
    {
        var result = await sender.Send(new GetPublishedScreensByModuleQuery(moduleCode), ct);
        return Ok(ApiResponse<List<FormScreenDto>>.Ok(result.Value));
    }

    [AllowAnonymous]
    [HttpGet("{moduleCode}")]
    public async Task<IActionResult> GetForms(string moduleCode, CancellationToken ct)
    {
        var result = await sender.Send(new GetFormsByModuleQuery(moduleCode), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse<List<FormTemplateDto>>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<List<FormTemplateDto>>.Ok(result.Value));
    }

    /// <summary>BDUI — trả schema để frontend render form động.</summary>
    [AllowAnonymous]
    [HttpGet("{moduleCode}/{formKey}/schema")]
    public async Task<IActionResult> GetSchema(string moduleCode, string formKey, CancellationToken ct)
    {
        var result = await sender.Send(new GetFormSchemaQuery(moduleCode, formKey), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse<FormSchemaDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<FormSchemaDto>.Ok(result.Value));
    }

    [AllowAnonymous]
    [HttpPost("{moduleCode}/{formKey}/submit")]
    public async Task<IActionResult> Submit(
        string               moduleCode,
        string               formKey,
        [FromBody] SubmitFormBody body,
        CancellationToken    ct)
    {
        var userId = User.Identity?.IsAuthenticated == true
            ? Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null
            : null;

        var cmd = new SubmitFormCommand(moduleCode, formKey, userId, body.Answers);
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<SubmitFormResultDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<SubmitFormResultDto>.Ok(result.Value));
    }

    [AllowAnonymous]
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "OK", service = "DynamicFormService", at = DateTime.UtcNow });
}

public sealed record SubmitFormBody(List<FieldAnswerInputDto> Answers);
