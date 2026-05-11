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
[Authorize]
public sealed class AuthController : ControllerBase
{
    private readonly ISender _sender;

    public AuthController(ISender sender) => _sender = sender;

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterUserCommand cmd, CancellationToken ct)
    {
        var result = await _sender.Send(cmd, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<UserDto>.Ok(result.Value))
            : BadRequest(ApiResponse<UserDto>.Fail(result.Error.Code, result.Error.Message));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginUserCommand cmd, CancellationToken ct)
    {
        var result = await _sender.Send(cmd, ct);
        if (result.IsFailure)
            return Unauthorized(ApiResponse<LoginResultDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<LoginResultDto>.Ok(result.Value));
    }

    [HttpGet("users/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _sender.Send(new GetUserByIdQuery(id), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse<UserDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<UserDto>.Ok(result.Value));
    }

    [HttpGet("validate")]
    public IActionResult ValidateToken() => Ok();

    [AllowAnonymous]
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "OK", service = "AuthService", at = DateTime.UtcNow });
}
