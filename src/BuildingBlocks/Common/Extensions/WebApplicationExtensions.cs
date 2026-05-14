using Hdos.Common.HealthChecks;
using Hdos.Common.Middleware;
using Microsoft.AspNetCore.Builder;
using Prometheus;

namespace Hdos.Common.Extensions;

public static class WebApplicationExtensions
{
    public static IApplicationBuilder UseHdosMiddleware(this IApplicationBuilder app)
    {
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseHttpMetrics(); // prometheus-net: track HTTP duration, status, method per request
        return app;
    }

    /// <summary>
    /// Reads X-User-Permissions header (injected by nginx from AuthService /auth/validate)
    /// and maps each value to a "permission" claim. Call between UseAuthentication() and UseAuthorization().
    /// </summary>
    public static IApplicationBuilder UseHdosPermissions(this IApplicationBuilder app)
        => app.UseMiddleware<PermissionsMiddleware>();

    public static IApplicationBuilder UseHdosCors(this IApplicationBuilder app)
    {
        return app.UseCors(ServiceCollectionExtensions.HdosCorsPolicy);
    }

    public static WebApplication UseHdosMonitoring(this WebApplication app)
    {
        app.MapMetrics();          // prometheus-net: expose /metrics for Prometheus scraping
        app.MapHdosHealthChecks(); // /health/live + /health/ready
        return app;
    }
}
