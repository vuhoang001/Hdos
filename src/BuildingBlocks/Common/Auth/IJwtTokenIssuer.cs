namespace Hdos.Common.Auth;

public interface IJwtTokenIssuer
{
    /// <summary>Phát access token chứa sub, email, name, roles (claim "roles").</summary>
    JwtTokenResult Issue(Guid userId, string email, string fullName, IEnumerable<string> roles);
}

public sealed record JwtTokenResult(string Token, DateTime ExpiresAtUtc);
