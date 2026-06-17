using Hdos.AuthService.Application.Features.SupersetGuestToken;
using Hdos.AuthService.Application.Options;
using Hdos.Common.Auth;
using Hdos.Common.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hdos.AuthService.API.Controllers;

[ApiController]
[Route("auth/superset")]
public sealed class SupersetController(
    ISender sender,
    IOptions<SupersetOptions> supersetOptions,
    IOptions<JwtOptions> jwtOptions)
    : ControllerBase
{
    /// <summary>
    /// Single sign-on tới Superset. Validate JWT hiện tại (qua Authorize),
    /// set cookie `hdos_jwt` scope `/superset/` để Security Manager Python
    /// (security_manager.py) auto-login user khi browser navigate sang Superset.
    /// </summary>
    /// <remarks>
    /// FE flow:
    /// <code>
    /// const res = await fetch('/auth/superset/sso', {
    ///   method: 'POST',
    ///   headers: { Authorization: `Bearer ${jwt}` },
    ///   credentials: 'include',
    /// });
    /// const { data } = await res.json();
    /// window.location.href = data.redirectUrl;
    /// </code>
    /// </remarks>
    [Authorize]
    [HttpPost("sso")]
    public IActionResult Sso()
    {
        var token = ExtractBearerToken();
        if (string.IsNullOrEmpty(token))
            return Unauthorized(ApiResponse.Fail("auth.no_token", "Missing bearer token"));

        Response.Cookies.Append("hdos_jwt", token, new CookieOptions
        {
            Path = "/superset/",
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddMinutes(jwtOptions.Value.ExpiresMinutes),
        });

        var publicUrl = supersetOptions.Value.PublicUrl;
        return Ok(ApiResponse<object>.Ok(new { redirectUrl = publicUrl }));
    }

    /// <summary>
    /// Phát hành Superset guest token cho FE nhúng dashboard qua iframe.
    /// FE gửi dashboardId + thông tin user → BE gọi Superset admin API issue token.
    /// Token TTL ~5 phút (configurable trong superset_config.py).
    /// </summary>
    [Authorize]
    [HttpPost("guest-token")]
    public async Task<IActionResult> GuestToken(
        [FromBody] CreateGuestTokenCommand cmd,
        CancellationToken ct)
    {
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<GuestTokenDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<GuestTokenDto>.Ok(result.Value!));
    }

    /// <summary>Logout phía Superset: xóa cookie `hdos_jwt`.</summary>
    [Authorize]
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("hdos_jwt", new CookieOptions { Path = "/superset/" });
        return Ok(ApiResponse.Ok());
    }

    private string? ExtractBearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)) return null;
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var token = header[prefix.Length..].Trim();
        return string.IsNullOrEmpty(token) ? null : token;
    }
}
