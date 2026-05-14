using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Application.Features.Admin.UserRoles;
using Hdos.Common.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.AuthService.API.Controllers;

[ApiController]
[Route("auth/admin/users")]
[Authorize(Roles = "admin")]
public sealed class UserRolesAdminController(ISender sender) : ControllerBase
{
    [HttpGet("{userId:guid}/roles")]
    public async Task<IActionResult> GetRoles(Guid userId, CancellationToken ct)
    {
        var list = await sender.Send(new GetUserRolesQuery(userId), ct);
        return Ok(ApiResponse<IReadOnlyList<UserRoleDto>>.Ok(list));
    }

    [HttpPost("{userId:guid}/roles/{roleId:guid}")]
    public async Task<IActionResult> AssignRole(Guid userId, Guid roleId, CancellationToken ct)
    {
        var result = await sender.Send(new AssignRoleToUserCommand(userId, roleId), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse.Ok());
    }

    [HttpDelete("{userId:guid}/roles/{roleId:guid}")]
    public async Task<IActionResult> RevokeRole(Guid userId, Guid roleId, CancellationToken ct)
    {
        var result = await sender.Send(new RevokeRoleFromUserCommand(userId, roleId), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse.Ok());
    }
}
