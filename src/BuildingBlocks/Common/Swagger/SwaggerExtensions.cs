using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace Hdos.Common.Swagger;

public static class SwaggerExtensions
{
    /// <summary>
    /// Đăng ký Swashbuckle với:
    /// - OAuth2 Authorization Code + PKCE (Keycloak) khi truyền keycloakPublicAuthority — click Authorize, login Keycloak, token tự điền.
    /// - JWT Bearer paste thủ công làm fallback (dùng được với /auth/login).
    /// </summary>
    public static IServiceCollection AddHdosSwagger(
        this IServiceCollection services,
        string serviceName,
        string? keycloakPublicAuthority = null,
        string version = "v1")
    {
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc(version, new OpenApiInfo { Title = $"Hdos {serviceName}", Version = version });

            if (!string.IsNullOrWhiteSpace(keycloakPublicAuthority))
            {
                c.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new Uri($"{keycloakPublicAuthority}/protocol/openid-connect/auth"),
                            TokenUrl         = new Uri($"{keycloakPublicAuthority}/protocol/openid-connect/token"),
                            Scopes = new Dictionary<string, string>
                            {
                                ["openid"]  = "OpenID Connect",
                                ["profile"] = "Profile",
                                ["email"]   = "Email"
                            }
                        }
                    }
                });

                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oauth2" }
                    }] = new[] { "openid", "profile", "email" }
                });
            }

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name         = "Authorization",
                Type         = SecuritySchemeType.Http,
                Scheme       = "bearer",
                BearerFormat = "JWT",
                In           = ParameterLocation.Header,
                Description  = "Fallback: paste accessToken thu được từ POST /auth/login vào đây."
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                }] = Array.Empty<string>()
            });
        });

        return services;
    }

    /// <summary>
    /// Cấu hình Swagger UI với route prefix chuẩn và OAuth2 PKCE client.
    /// Gọi thay cho UseSwagger() + UseSwaggerUI() thủ công.
    /// </summary>
    public static void UseHdosSwaggerUI(
        this WebApplication app,
        string servicePrefix,
        string serviceTitle,
        string oauthClientId = "hdos-frontend")
    {
        app.UseSwagger(c => c.RouteTemplate = $"{servicePrefix}/swagger/{{documentName}}/swagger.json");
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint($"/{servicePrefix}/swagger/v1/swagger.json", serviceTitle);
            c.RoutePrefix = $"{servicePrefix}/swagger";
            c.OAuthClientId(oauthClientId);
            c.OAuthUsePkce();
            c.OAuthScopes("openid", "profile", "email");
        });
    }
}
