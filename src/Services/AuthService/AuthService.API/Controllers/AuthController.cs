using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Application.Features.GetUser;
using Hdos.AuthService.Application.Features.ValidateToken;
using Hdos.Common.Auth;
using Hdos.Common.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Json;

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

    /// <summary>
    /// Lấy JWT token từ Keycloak bằng username/password.
    /// Dùng để test API qua Swagger — copy accessToken rồi paste vào ô Authorize (Bearer).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] IOptions<KeycloakOptions> keycloakOptions,
        CancellationToken ct)
    {
        var opts     = keycloakOptions.Value;
        var tokenUrl = $"{opts.Authority}/protocol/openid-connect/token";

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"]  = opts.ClientId,
            ["username"]   = request.Username,
            ["password"]   = request.Password,
            ["scope"]      = "openid"
        };

        var http     = httpClientFactory.CreateClient();
        var response = await http.PostAsync(tokenUrl, new FormUrlEncodedContent(form), ct);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            return Unauthorized(new { error = err });
        }

        var json        = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var accessToken = json.GetProperty("access_token").GetString()!;
        var tokenType   = json.GetProperty("token_type").GetString()!;
        var expiresIn   = json.GetProperty("expires_in").GetInt32();
        var refreshToken = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;

        return Ok(new { accessToken, tokenType, expiresIn, refreshToken });
    }

    [AllowAnonymous]
    [HttpGet("health")]
    public IActionResult Health() =>
        Ok(new { status = "OK", service = "AuthService", at = DateTime.UtcNow });
}

public sealed record LoginRequest(string Username, string Password);
