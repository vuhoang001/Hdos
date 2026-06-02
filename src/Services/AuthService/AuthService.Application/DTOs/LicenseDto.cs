namespace Hdos.AuthService.Application.DTOs;

public sealed record LicenseDto(
    Guid Id,
    Guid UserId,
    string Plan,
    IReadOnlyList<string> Modules,
    DateTime? ExpiresAtUtc,
    bool IsActive,
    bool IsExpired,
    DateTime CreatedAtUtc);
