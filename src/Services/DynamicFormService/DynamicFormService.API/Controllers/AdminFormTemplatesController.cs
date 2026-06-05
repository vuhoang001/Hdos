using Hdos.Common.Responses;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Forms.AddField;
using Hdos.DynamicFormService.Application.Features.Forms.ArchiveForm;
using Hdos.DynamicFormService.Application.Features.Forms.CreateForm;
using Hdos.DynamicFormService.Application.Features.Forms.PublishForm;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DynamicFormService.API.Controllers;

[ApiController]
[Route("forms/admin")]
// [Authorize(Policy = HdosPermissions.FormsAdmin)]
public sealed class AdminFormTemplatesController(ISender sender) : ControllerBase
{
    [HttpPost("modules/{moduleCode}/forms")]
    public async Task<IActionResult> Create(
        string                    moduleCode,
        [FromBody] CreateFormBody body,
        CancellationToken         ct)
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
        Guid                       formTemplateId,
        [FromBody] AddFieldCommand cmd,
        CancellationToken          ct)
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
}

public sealed record CreateFormBody(
    string  Key,
    string  Name,
    string? Description,
    string  SubmitButtonLabel        = "Gửi",
    string  SuccessMessage           = "Đã gửi form thành công",
    bool    AllowMultipleSubmissions = true);
