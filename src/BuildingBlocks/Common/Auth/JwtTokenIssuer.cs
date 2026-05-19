using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Hdos.Common.Auth;

public sealed class JwtTokenIssuer : IJwtTokenIssuer
{
    private readonly JwtOptions _options;

    public JwtTokenIssuer(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.Secret) || _options.Secret.Length < 32)
            throw new InvalidOperationException(
                "Jwt:Secret phải >= 32 ký tự. Set qua env Jwt__Secret.");
    }

    public JwtTokenResult Issue(Guid userId, string email, string fullName, IEnumerable<string> roles)
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
