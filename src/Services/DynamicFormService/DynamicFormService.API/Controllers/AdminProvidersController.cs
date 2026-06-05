using Hdos.Common.Responses;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Providers.CreateProvider;
using Hdos.DynamicFormService.Application.Features.Providers.DeleteProvider;
using Hdos.DynamicFormService.Application.Features.Providers.GetProviderByCode;
using Hdos.DynamicFormService.Application.Features.Providers.GetProviders;
using Hdos.DynamicFormService.Application.Features.Providers.UpdateProvider;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DynamicFormService.API.Controllers;

[ApiController]
[Route("forms/admin/providers")]
// [Authorize(Policy = HdosPermissions.FormsAdmin)]
public sealed class AdminProvidersController(ISender sender) : ControllerBase
{
    /// <summary>Tạo provider mới (đăng ký 1 service nguồn dữ liệu).</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProviderCommand cmd, CancellationToken ct)
    {
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return result.Error.Code == "Conflict"
                ? Conflict(ApiResponse<ProviderDto>.Fail(result.Error.Code, result.Error.Message))
                : BadRequest(ApiResponse<ProviderDto>.Fail(result.Error.Code, result.Error.Message));
        return CreatedAtAction(nameof(GetByCode), new { code = result.Value.Code },
            ApiResponse<ProviderDto>.Ok(result.Value));
    }

    /// <summary>Danh sách providers, optional filter ?status=active|inactive.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
    {
        var result = await sender.Send(new GetProvidersQuery(status), ct);
        return Ok(ApiResponse<List<ProviderDto>>.Ok(result.Value));
    }

    /// <summary>Lấy provider theo code.</summary>
    [HttpGet("{code}")]
    public async Task<IActionResult> GetByCode(string code, CancellationToken ct)
    {
        var result = await sender.Send(new GetProviderByCodeQuery(code), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<ProviderDto>.Ok(result.Value))
            : NotFound(ApiResponse<ProviderDto>.Fail(result.Error.Code, result.Error.Message));
    }

    /// <summary>Update displayName, baseUrl, status của provider.</summary>
    [HttpPut("{code}")]
    public async Task<IActionResult> Update(
        string                       code,
        [FromBody] UpdateProviderBody body,
        CancellationToken            ct)
    {
        var cmd    = new UpdateProviderCommand(code, body.DisplayName, body.BaseUrl, body.Status);
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return result.Error.Code == "NotFound"
                ? NotFound(ApiResponse<ProviderDto>.Fail(result.Error.Code, result.Error.Message))
                : BadRequest(ApiResponse<ProviderDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<ProviderDto>.Ok(result.Value));
    }

    /// <summary>Xóa provider — Conflict nếu còn Operation đang dùng.</summary>
    [HttpDelete("{code}")]
    public async Task<IActionResult> Delete(string code, CancellationToken ct)
    {
        var result = await sender.Send(new DeleteProviderCommand(code), ct);
        if (result.IsFailure)
            return result.Error.Code == "NotFound"
                ? NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message))
                : Conflict(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return NoContent();
    }
}

public sealed record UpdateProviderBody(string DisplayName, string BaseUrl, string? Status);
