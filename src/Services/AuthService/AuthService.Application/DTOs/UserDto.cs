namespace Hdos.AuthService.Application.DTOs;

public sealed record UserDto(Guid Id, string Email, string FullName, DateTime CreatedAtUtc);
public sealed record LoginResultDto(Guid UserId, string Email, string Token);
