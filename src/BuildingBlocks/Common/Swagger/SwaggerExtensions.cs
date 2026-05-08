using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace Hdos.Common.Swagger;

public static class SwaggerExtensions
{
    /// <summary>
    /// Registers Swashbuckle with a JWT Bearer security scheme so the generated
    /// Swagger UI shows the "Authorize" button. The token is applied as a global
    /// requirement — endpoints marked <c>[AllowAnonymous]</c> still work without
    /// it, but every other endpoint shows the padlock icon and reuses the token
    /// pasted into the dialog.
    /// </summary>
    public static IServiceCollection AddHdosSwagger(
        this IServiceCollection services,
        string serviceName,
        string version = "v1")
    {
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc(version, new OpenApiInfo
            {
                Title = $"Hdos {serviceName}",
                Version = version
            });

            const string scheme = "Bearer";
            c.AddSecurityDefinition(scheme, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste only the JWT token here — Swagger will prepend \"Bearer \" automatically."
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = scheme
                    }
                }] = Array.Empty<string>()
            });
        });

        return services;
    }
}
