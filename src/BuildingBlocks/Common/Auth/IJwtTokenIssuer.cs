namespace Hdos.Common.Auth;

public interface IJwtTokenIssuer
{
    JwtTokenResult Issue(Guid userId, string email);
}

public sealed record JwtTokenResult(string Token, DateTime ExpiresAtUtc);
