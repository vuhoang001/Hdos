using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hdos.Common.HealthChecks;

public static class HealthCheckExtensions
{
    public static IServiceCollection AddHdosHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration,
        string? sqlConnectionStringKey = null,
        bool checkRabbitMq = false)
    {
        var builder = services.AddHealthChecks();

        services.AddSingleton<IHealthCheckPublisher, PrometheusHealthPublisher>();
        services.Configure<HealthCheckPublisherOptions>(opts => opts.Period = TimeSpan.FromSeconds(15));

        if (sqlConnectionStringKey is not null)
        {
            var connectionString = configuration.GetConnectionString(sqlConnectionStringKey) ?? "";
            builder.AddSqlServer(
                connectionString,
                name: "sqlserver",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["db"]);
        }

        // MassTransit tự đăng ký health check cho bus khi gọi AddMassTransit().
        // Parameter checkRabbitMq được giữ lại để tương thích API nhưng không cần thao tác thêm.

        return services;
    }

    public static WebApplication MapHdosHealthChecks(this WebApplication app)
    {
        // Liveness: always 200 — app process is alive
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        }).AllowAnonymous();

        // Readiness: checks all dependencies (DB, RabbitMQ)
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = WriteJsonResponseAsync
        }).AllowAnonymous();

        // Combined shortcut: /health → same as /health/ready
        // Dùng bởi gateway route /xxx/health → /health sau khi strip prefix
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = WriteJsonResponseAsync
        }).AllowAnonymous();

        return app;
    }

    private static async Task WriteJsonResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = e.Value.Duration.TotalMilliseconds,
                tags = e.Value.Tags
            })
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response,
            new JsonSerializerOptions { WriteIndented = false }));
    }
}
