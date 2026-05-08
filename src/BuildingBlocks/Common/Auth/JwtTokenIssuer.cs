using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Hdos.Common.Auth;

public sealed class JwtTokenIssuer : IJwtTokenIssuer
{
    private readonly JwtOptions _options;

    public JwtTokenIssuer(IOptions<JwtOptions> options) => _options = options.Value;

    public JwtTokenResult Issue(Guid userId, string email)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.ExpiresMinutes);
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret)),
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

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
