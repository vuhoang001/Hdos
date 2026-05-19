using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Application.Features.GetUser;
using Hdos.AuthService.Application.Features.Login;
using Hdos.AuthService.Application.Features.Register;
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
    /// <summary>Đăng ký user mới.</summary>
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterUserCommand cmd, CancellationToken ct)
    {
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<UserDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<UserDto>.Ok(result.Value!));
    }

    /// <summary>Đăng nhập bằng email + password, trả access token JWT (HS256).</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginUserCommand cmd, CancellationToken ct)
    {
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return Unauthorized(ApiResponse<LoginResultDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<LoginResultDto>.Ok(result.Value!));
    }

    /// <summary>
    /// Gọi bởi nginx auth_request trên mọi request có bảo vệ.
    /// Validate JWT (qua JwtBearer middleware), resolve roles + permissions từ DB,
    /// ghi vào response headers để nginx forward sang upstream services.
    /// </summary>
    [Authorize]
    [HttpGet("validate")]
    public async Task<IActionResult> Validate(CancellationToken ct)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");
        if (sub is null || !Guid.TryParse(sub, out var userId))
            return Unauthorized();

        var result = await sender.Send(new ValidateAndResolveQuery(userId), ct);
        if (result.IsFailure)
            return Unauthorized();

        Response.Headers["X-User-Id"]          = userId.ToString();
        Response.Headers["X-User-Email"]        = User.FindFirstValue("email")
                                                  ?? User.FindFirstValue(ClaimTypes.Email)
                                                  ?? string.Empty;
        Response.Headers["X-User-Roles"]        = string.Join(",", result.Value!.Roles);
        Response.Headers["X-User-Permissions"]  = string.Join(",", result.Value!.Permissions);

        return Ok();
    }

    /// <summary>Lấy thông tin user theo ID (chỉ admin).</summary>
    [Authorize(Roles = "admin")]
    [HttpGet("users/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new GetUserByIdQuery(id), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse<UserDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<UserDto>.Ok(result.Value!));
    }

    [AllowAnonymous]
    [HttpGet("health")]
    public IActionResult Health() =>
        Ok(new { status = "OK", service = "AuthService", at = DateTime.UtcNow });
}
