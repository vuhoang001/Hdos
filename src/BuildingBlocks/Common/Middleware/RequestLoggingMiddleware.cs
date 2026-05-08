using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Hdos.Common.Middleware;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Push TraceId/SpanId into Serilog log context so every log entry
        // during this request carries them — enables trace-to-log correlation in Grafana.
        var activity = Activity.Current;
        using var traceScope = activity is not null
            ? LogContext.PushProperty("TraceId", activity.TraceId.ToString())
            : null;
        using var spanScope = activity is not null
            ? LogContext.PushProperty("SpanId", activity.SpanId.ToString())
            : null;

        var sw = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            _logger.LogInformation(
                "HTTP {Method} {Path} responded {Status} in {Elapsed}ms",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                sw.ElapsedMilliseconds);
        }
    }
}
