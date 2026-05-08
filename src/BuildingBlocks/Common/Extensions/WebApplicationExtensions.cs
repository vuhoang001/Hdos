using Hdos.Common.Middleware;
using Microsoft.AspNetCore.Builder;

namespace Hdos.Common.Extensions;

public static class WebApplicationExtensions
{
    public static IApplicationBuilder UseHdosMiddleware(this IApplicationBuilder app)
    {
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();
        return app;
    }
}
