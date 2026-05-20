namespace Hdos.Common.Auth;

public interface IJwtTokenIssuer
{
    /// <summary>
    /// Phát access token chứa sub, email, name, roles (claim "roles") và permissions (claim "permission").
    /// Mỗi service tự validate token + đọc claim "permission" để enforce policy — không cần round-trip về AuthService.
    /// </summary>
    JwtTokenResult Issue(
        Guid userId,
        string email,
        string fullName,
        IEnumerable<string> roles,
        IEnumerable<string> permissions);
}

public sealed record JwtTokenResult(string Token, DateTime ExpiresAtUtc);
