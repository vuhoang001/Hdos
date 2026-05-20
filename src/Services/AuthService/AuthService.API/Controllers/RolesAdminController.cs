using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Application.Features.Admin.Roles;
using Hdos.Common.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.AuthService.API.Controllers;

[ApiController]
[Route("auth/admin/roles")]
// [Authorize(Roles = "admin")]
public sealed class RolesAdminController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var list = await sender.Send(new GetRolesQuery(), ct);
        return Ok(ApiResponse<IReadOnlyList<RoleDto>>.Ok(list));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoleCommand cmd, CancellationToken ct)
    {
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return Conflict(ApiResponse<RoleDto>.Fail(result.Error.Code, result.Error.Message));
        return CreatedAtAction(nameof(GetAll), ApiResponse<RoleDto>.Ok(result.Value));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRoleRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new UpdateRoleCommand(id, req.Name, req.Description), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse<RoleDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<RoleDto>.Ok(result.Value));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new DeleteRoleCommand(id), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse.Ok());
    }
}

public sealed record UpdateRoleRequest(string Name, string Description);
