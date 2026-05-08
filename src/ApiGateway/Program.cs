using Hdos.Common.Auth;
using Hdos.Common.Extensions;
using Hdos.Common.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.UseHdosLogging("ApiGateway");

builder.Services.AddHdosJwtAuth(builder.Configuration);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseHdosMiddleware();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    name = "Hdos API Gateway",
    routes = new[] { "/auth/*", "/orders/*", "/notifications/*", "/health" }
})).AllowAnonymous();

app.MapGet("/health", () => Results.Ok(new { status = "OK", service = "ApiGateway" }))
    .AllowAnonymous();

app.MapReverseProxy();

app.Run();
