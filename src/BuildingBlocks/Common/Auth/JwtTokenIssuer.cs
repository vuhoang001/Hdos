using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Hdos.Common.Auth;

/// <summary>
/// Triển khai <see cref="IJwtTokenIssuer"/> dùng HS256 (HMAC-SHA256) với shared secret.
/// Đăng ký là Singleton trong DI qua <c>AddHdosJwtAuth()</c>.
/// </summary>
/// <remarks>
/// Claims được nhúng vào JWT:
/// <list type="table">
///   <listheader><term>Claim</term><description>Nguồn</description></listheader>
///   <item><term><c>sub</c></term><description><paramref name="userId"/></description></item>
///   <item><term><c>email</c>, <c>preferred_username</c></term><description><paramref name="email"/></description></item>
///   <item><term><c>name</c></term><description><paramref name="fullName"/></description></item>
///   <item><term><c>jti</c></term><description>GUID ngẫu nhiên — unique per token</description></item>
///   <item><term><c>roles</c></term><description>Mỗi role một claim riêng</description></item>
///   <item><term><c>permission</c></term><description>Mỗi permission một claim riêng</description></item>
///   <item><term><c>lic_plan</c>, <c>lic_mod</c>, <c>lic_exp</c></term><description>Từ <see cref="LicenseInfo"/> nếu có</description></item>
/// </list>
/// </remarks>
public sealed class JwtTokenIssuer : IJwtTokenIssuer
{
    private readonly JwtOptions _options;

    /// <exception cref="InvalidOperationException">
    /// Ném nếu <c>Jwt:Secret</c> chưa được set hoặc ngắn hơn 32 ký tự.
    /// </exception>
    public JwtTokenIssuer(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.Secret) || _options.Secret.Length < 32)
            throw new InvalidOperationException(
                "Jwt:Secret phải >= 32 ký tự. Set qua env Jwt__Secret.");
    }

    /// <inheritdoc/>
    public JwtTokenResult Issue(
        Guid userId,
        string email,
        string fullName,
        IEnumerable<string> roles,
        IEnumerable<string> permissions,
        LicenseInfo? license = null)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.ExpiresMinutes);
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new("name", fullName),
            new("preferred_username", email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        foreach (var r in roles.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            claims.Add(new Claim("roles", r));
        foreach (var p in permissions.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            claims.Add(new Claim("permission", p));

        if (license is not null)
        {
            claims.Add(new Claim(LicenseClaimTypes.Plan, license.Plan));
            foreach (var mod in license.Modules.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
                claims.Add(new Claim(LicenseClaimTypes.Module, mod));
            if (license.ExpiresAtUtc.HasValue)
                claims.Add(new Claim(LicenseClaimTypes.ExpiresAt,
                    license.ExpiresAtUtc.Value.ToString("O")));
        }

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: creds);

        return new JwtTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
