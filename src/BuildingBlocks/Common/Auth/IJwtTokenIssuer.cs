namespace Hdos.Common.Auth;

public interface IJwtTokenIssuer
{
    JwtTokenResult Issue(
        Guid userId,
        string email,
        string fullName,
        IEnumerable<string> roles,
        IEnumerable<string> permissions,
        LicenseInfo? license = null);
}

/// <param name="Plan">Tên plan: free | basic | pro | enterprise</param>
/// <param name="Modules">Danh sách module slugs được phép, xem <see cref="HdosModules"/>.</param>
/// <param name="ExpiresAtUtc">null = vĩnh viễn.</param>
public sealed record LicenseInfo(
    string Plan,
    IEnumerable<string> Modules,
    DateTime? ExpiresAtUtc);

public sealed record JwtTokenResult(string Token, DateTime ExpiresAtUtc);
