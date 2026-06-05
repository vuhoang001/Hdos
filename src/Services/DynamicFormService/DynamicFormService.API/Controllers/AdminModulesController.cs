using Hdos.Common.Responses;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Modules.CreateModule;
using Hdos.DynamicFormService.Application.Features.Modules.DeleteModule;
using Hdos.DynamicFormService.Application.Features.Modules.UpdateModule;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DynamicFormService.API.Controllers;

[ApiController]
[Route("forms/admin/modules")]
// [Authorize(Policy = HdosPermissions.FormsAdmin)]
public sealed class AdminModulesController(ISender sender) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateModuleCommand cmd, CancellationToken ct)
    {
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<FormModuleDto>.Fail(result.Error.Code, result.Error.Message));
        return CreatedAtAction(null, ApiResponse<FormModuleDto>.Ok(result.Value));
    }

    [HttpPut("{moduleCode}")]
    public async Task<IActionResult> Update(
        string                      moduleCode,
        [FromBody] UpdateModuleBody body,
        CancellationToken           ct)
    {
        var cmd    = new UpdateModuleCommand(moduleCode, body.Name, body.Description);
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<FormModuleDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<FormModuleDto>.Ok(result.Value));
    }

    [HttpDelete("{moduleCode}")]
    public async Task<IActionResult> Delete(string moduleCode, CancellationToken ct)
    {
        var result = await sender.Send(new DeleteModuleCommand(moduleCode), ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse.Ok());
    }
}

public sealed record UpdateModuleBody(string Name, string? Description);
