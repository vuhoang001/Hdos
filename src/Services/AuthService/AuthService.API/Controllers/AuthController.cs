using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Application.Features.GetUser;
using Hdos.AuthService.Application.Features.Login;
using Hdos.AuthService.Application.Features.Register;
using Hdos.Common.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    /// <summary>Lấy thông tin user theo ID (chỉ admin).</summary>
    // [Authorize(Roles = "admin")]
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
