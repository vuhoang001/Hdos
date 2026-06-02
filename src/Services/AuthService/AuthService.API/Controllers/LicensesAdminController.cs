using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Application.Features.License;
using Hdos.Common.Auth;
using Hdos.Common.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.AuthService.API.Controllers;

[ApiController]
[Route("auth/admin/licenses")]
[Authorize(Roles = "admin")]
public sealed class LicensesAdminController(ISender sender) : ControllerBase
{
    /// <summary>Lấy license đang active của user.</summary>
    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> Get(Guid userId, CancellationToken ct)
    {
        var result = await sender.Send(new GetUserLicenseQuery(userId), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse<LicenseDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<LicenseDto>.Ok(result.Value));
    }

    /// <summary>
    /// Gán hoặc thay thế license cho user.
    /// Nếu user đã có license active → tự động revoke và tạo mới.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Assign([FromBody] AssignLicenseRequest req, CancellationToken ct)
    {
        var result = await sender.Send(
            new AssignLicenseCommand(req.UserId, req.Plan, req.Modules, req.ExpiresAtUtc), ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<LicenseDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<LicenseDto>.Ok(result.Value));
    }

    /// <summary>Revoke license active của user.</summary>
    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> Revoke(Guid userId, CancellationToken ct)
    {
        var result = await sender.Send(new RevokeLicenseCommand(userId), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse.Ok());
    }
}

public sealed record AssignLicenseRequest(
    Guid UserId,
    string Plan,
    IEnumerable<string> Modules,
    DateTime? ExpiresAtUtc);
