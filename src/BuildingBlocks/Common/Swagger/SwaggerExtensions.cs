using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace Hdos.Common.Swagger;

public static class SwaggerExtensions
{
    /// <summary>
    /// Đăng ký Swashbuckle với JWT Bearer paste.
    /// Lấy token bằng cách gọi POST /auth/login (AuthService) rồi paste accessToken vào nút Authorize.
    /// </summary>
    public static IServiceCollection AddHdosSwagger(
        this IServiceCollection services,
        string serviceName,
        string version = "v1")
    {
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc(version, new OpenApiInfo { Title = $"Hdos {serviceName}", Version = version });

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name         = "Authorization",
                Type         = SecuritySchemeType.Http,
                Scheme       = "bearer",
                BearerFormat = "JWT",
                In           = ParameterLocation.Header,
                Description  = "Gọi POST /auth/login để lấy accessToken, paste vào đây (không cần prefix \"Bearer \")."
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

    /// <summary>Cấu hình Swagger UI với route prefix chuẩn.</summary>
    public static void UseHdosSwaggerUi(
        this WebApplication app,
        string servicePrefix,
        string serviceTitle)
    {
        app.UseSwagger(c => c.RouteTemplate = $"{servicePrefix}/swagger/{{documentName}}/swagger.json");
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint($"/{servicePrefix}/swagger/v1/swagger.json", serviceTitle);
            c.RoutePrefix = $"{servicePrefix}/swagger";
        });
    }
}
