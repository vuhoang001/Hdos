using Hdos.Common.Extensions;
using Hdos.Common.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.UseHdosLogging("ApiGateway");

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseHdosMiddleware();

app.MapGet("/", () => Results.Ok(new
{
    name = "Hdos API Gateway",
    routes = new[] { "/auth/*", "/orders/*", "/notifications/*", "/health" }
}));

app.MapGet("/health", () => Results.Ok(new { status = "OK", service = "ApiGateway" }));

app.MapReverseProxy();

app.Run();
