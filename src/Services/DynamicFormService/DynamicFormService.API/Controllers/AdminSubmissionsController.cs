using Hdos.Common.Responses;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Submissions.GetSubmissions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DynamicFormService.API.Controllers;

[ApiController]
[Route("forms/admin/forms/{formTemplateId:guid}/submissions")]
// [Authorize(Policy = HdosPermissions.FormsAdmin)]
public sealed class AdminSubmissionsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        Guid              formTemplateId,
        [FromQuery] int   page     = 1,
        [FromQuery] int   pageSize = 20,
        CancellationToken ct       = default)
    {
        var result = await sender.Send(new GetSubmissionsQuery(formTemplateId, page, pageSize), ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<List<FormSubmissionDto>>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<List<FormSubmissionDto>>.Ok(result.Value));
    }
}
