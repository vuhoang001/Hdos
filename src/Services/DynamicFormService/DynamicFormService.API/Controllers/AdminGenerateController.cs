using Hdos.Common.Responses;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Screens.GenerateFromSource;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DynamicFormService.API.Controllers;

[ApiController]
[Route("forms/admin/generate-from-source")]
// [Authorize(Policy = HdosPermissions.FormsAdmin)]
public sealed class AdminGenerateController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Tự động tạo Module + Screen + DataSources + Form + Fields + Tab + Widget từ một lệnh duy nhất.
    /// Fields có CanonicalKey sẽ được tự động bind expression {{sources.namespace.CanonicalKey}}.
    /// Screen và Form đều được publish ngay, sẵn sàng render.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> GenerateFromSource(
        [FromBody] GenerateFromSourceCommand cmd,
        CancellationToken                    ct)
    {
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<GenerateFromSourceResultDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<GenerateFromSourceResultDto>.Ok(result.Value));
    }
}
