using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Application.Features.GetUser;
using Hdos.AuthService.Application.Features.ValidateToken;
using Hdos.Common.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Hdos.AuthService.API.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Called by nginx auth_request on every protected request.
    /// Validates the Keycloak JWT (via JwtBearer middleware), JIT-provisions the user profile,
    /// resolves RBAC roles + permissions from the AuthService DB, and writes them to response
    /// headers so nginx can forward them to upstream services.
    /// </summary>
    [Authorize]
    [HttpGet("validate")]
    public async Task<IActionResult> Validate(CancellationToken ct)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");
        if (sub is null || !Guid.TryParse(sub, out var userId))
            return Unauthorized();

        var email    = User.FindFirstValue("email")
                  ?? User.FindFirstValue(ClaimTypes.Email)
                  ?? string.Empty;
        var fullName = User.FindFirstValue("preferred_username")
                       ?? User.FindFirstValue("name")
                       ?? email;

        var ctx = await sender.Send(new ValidateAndResolveQuery(userId, email, fullName), ct);

        Response.Headers["X-User-Id"]          = userId.ToString();
        Response.Headers["X-User-Email"]        = email;
        Response.Headers["X-User-Roles"]        = string.Join(",", ctx.Roles);
        Response.Headers["X-User-Permissions"]  = string.Join(",", ctx.Permissions);

        return Ok();
    }

    /// <summary>Returns a user's local profile (keyed by Keycloak sub).</summary>
    [Authorize(Roles = "admin")]
    [HttpGet("users/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new GetUserByIdQuery(id), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse<UserDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<UserDto>.Ok(result.Value));
    }

    [AllowAnonymous]
    [HttpGet("health")]
    public IActionResult Health() =>
        Ok(new { status = "OK", service = "AuthService", at = DateTime.UtcNow });
}
