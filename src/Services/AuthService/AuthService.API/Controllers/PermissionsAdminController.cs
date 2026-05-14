using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Application.Features.Admin.Permissions;
using Hdos.Common.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.AuthService.API.Controllers;

[ApiController]
[Route("auth/admin/permissions")]
[Authorize(Roles = "admin")]
public sealed class PermissionsAdminController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var list = await sender.Send(new GetPermissionsQuery(), ct);
        return Ok(ApiResponse<IReadOnlyList<PermissionDto>>.Ok(list));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePermissionCommand cmd, CancellationToken ct)
    {
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return Conflict(ApiResponse<PermissionDto>.Fail(result.Error.Code, result.Error.Message));
        return CreatedAtAction(nameof(GetAll), ApiResponse<PermissionDto>.Ok(result.Value));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new DeletePermissionCommand(id), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse.Ok());
    }

    [HttpPost("{roleId:guid}/permissions/{permissionId:guid}")]
    public async Task<IActionResult> AssignToRole(Guid roleId, Guid permissionId, CancellationToken ct)
    {
        var result = await sender.Send(new AssignPermissionToRoleCommand(roleId, permissionId), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse.Ok());
    }

    [HttpDelete("{roleId:guid}/permissions/{permissionId:guid}")]
    public async Task<IActionResult> RevokeFromRole(Guid roleId, Guid permissionId, CancellationToken ct)
    {
        var result = await sender.Send(new RevokePermissionFromRoleCommand(roleId, permissionId), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse.Ok());
    }
}
