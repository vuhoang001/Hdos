using Hdos.Common.Responses;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Pages.GetPageSchema;
using Hdos.DynamicFormService.Application.Features.Pages.GetPages;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DynamicFormService.API.Controllers;

[ApiController]
[Route("forms/pages")]
public sealed class PagesController(ISender sender) : ControllerBase
{
    /// <summary>BDUI — trả layout + schema của tất cả form trong page, frontend render một màn hình.</summary>
    [AllowAnonymous]
    [HttpGet("{moduleCode}/{pageCode}")]
    public async Task<IActionResult> GetPageSchema(string moduleCode, string pageCode, CancellationToken ct)
    {
        var result = await sender.Send(new GetPageSchemaQuery(moduleCode, pageCode), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse<FormPageSchemaDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<FormPageSchemaDto>.Ok(result.Value));
    }

    [AllowAnonymous]
    [HttpGet("{moduleCode}")]
    public async Task<IActionResult> GetPages(string moduleCode, CancellationToken ct)
    {
        var result = await sender.Send(new GetPagesByModuleQuery(moduleCode), ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<List<FormPageDto>>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<List<FormPageDto>>.Ok(result.Value));
    }
}
