using Hdos.AuthService.Application.Features.SupersetGuestToken;
using Hdos.AuthService.Application.Options;
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
    IOptions<SupersetOptions> supersetOptions)
    : ControllerBase
{
    /// <summary>
    /// Single sign-on tới Superset (port 8444 dedicated).
    /// Trả redirectUrl = {publicUrl}?access_token={jwt} → Security Manager Python
    /// (security_manager.py) đọc query param khi browser navigate, auto-login user.
    /// Sau lần redirect đầu, Superset session cookie riêng take over → các request
    /// sau không còn ?access_token= trong URL.
    /// </summary>
    /// <remarks>
    /// Cookie không dùng được vì Superset ở port riêng (8444), browser không
    /// gửi cookie set ở :8443 cross-port. Query param chấp nhận trong môi trường
    /// LAN nội bộ (JWT chỉ exposed 1 lần trên URL redirect, ngắn hạn).
    ///
    /// FE flow:
    /// <code>
    /// const res = await fetch('/auth/superset/sso', {
    ///   method: 'POST',
    ///   headers: { Authorization: `Bearer ${jwt}` },
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

        var publicUrl = supersetOptions.Value.PublicUrl;
        var separator = publicUrl.Contains('?') ? '&' : '?';
        var redirectUrl = $"{publicUrl}{separator}access_token={Uri.EscapeDataString(token)}";

        return Ok(ApiResponse<object>.Ok(new { redirectUrl }));
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

    /// <summary>
    /// Logout phía Superset. Trả redirectUrl tới Superset logout endpoint —
    /// FE navigate tới đây để xóa session cookie của Superset (set bởi Flask-AppBuilder).
    /// </summary>
    [Authorize]
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        var publicUrl = supersetOptions.Value.PublicUrl.TrimEnd('/');
        return Ok(ApiResponse<object>.Ok(new { redirectUrl = $"{publicUrl}/logout/" }));
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
