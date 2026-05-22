using System.Net;
using System.Text.Json;
using Hdos.Common.Exceptions;
using Hdos.Common.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Hdos.Common.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception while processing {Path}", context.Request.Path);
            await WriteErrorAsync(context, ex);
        }
    }

    private static Task WriteErrorAsync(HttpContext context, Exception ex)
    {
        var (status, code, message) = ex switch
        {
            ValidationException v => (HttpStatusCode.BadRequest, "Validation",
                JsonSerializer.Serialize(v.Errors)),
            NotFoundException nf => (HttpStatusCode.NotFound, "NotFound", nf.Message),
            ConflictException cf => (HttpStatusCode.Conflict, "Conflict", cf.Message),
            UnauthorizedAccessException ua => (HttpStatusCode.Unauthorized, "Unauthorized", ua.Message),
            _ => (HttpStatusCode.InternalServerError, "Server", "An unexpected error occurred.")
        };

        context.Response.StatusCode  = (int)status;
        context.Response.ContentType = "application/json";
        var payload = ApiResponse.Fail(code, message);
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}