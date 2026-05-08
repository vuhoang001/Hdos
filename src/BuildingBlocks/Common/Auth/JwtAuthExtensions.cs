using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Hdos.Common.Auth;

public static class JwtAuthExtensions
{
    /// <summary>
    /// Wires up JWT bearer authentication using the "Jwt" config section.
    /// Call this from Gateway and every service that should validate tokens.
    /// </summary>
    public static IServiceCollection AddHdosJwtAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(JwtOptions.SectionName);
        services.Configure<JwtOptions>(section);
        var options = section.Get<JwtOptions>() ?? new JwtOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.RequireHttpsMetadata = false;
                o.SaveToken = true;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(options.Secret)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization();
        return services;
    }

    /// <summary>
    /// Registers <see cref="IJwtTokenIssuer"/>. Only AuthService needs this.
    /// </summary>
    public static IServiceCollection AddHdosJwtIssuer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<IJwtTokenIssuer, JwtTokenIssuer>();
        return services;
    }
}
