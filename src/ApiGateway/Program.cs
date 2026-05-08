using Hdos.Common.Auth;
using Hdos.Common.Extensions;
using Hdos.Common.HealthChecks;
using Hdos.Common.Logging;
using Hdos.Common.Monitoring;

var builder = WebApplication.CreateBuilder(args);
builder.UseHdosLogging("ApiGateway");

builder.Services.AddHdosJwtAuth(builder.Configuration);
builder.Services.AddHdosCors();

builder.Services.AddHdosOpenTelemetry(builder.Configuration, "ApiGateway");
builder.Services.AddHdosHealthChecks(builder.Configuration);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseHdosMiddleware();

// SignalR (WebSocket) cần upgrade pipeline. YARP tự forward được upgrade nhưng
// gọi explicit để các middleware đứng trước không nuốt request.
app.UseWebSockets();

app.UseHdosCors();

app.UseAuthentication();
app.UseAuthorization();

// Swagger UI gộp 3 services. UI phục vụ tại /swagger/index.html.
// Mỗi document được gateway gọi xuống service qua YARP route /<svc>/swagger/v1/swagger.json.
app.UseSwaggerUI(c =>
{
    c.RoutePrefix = "swagger";
    c.DocumentTitle = "Hdos API — Aggregated Swagger";
    c.SwaggerEndpoint("/auth/swagger/v1/swagger.json", "AuthService v1");
    c.SwaggerEndpoint("/orders/swagger/v1/swagger.json", "OrderService v1");
    c.SwaggerEndpoint("/notifications/swagger/v1/swagger.json", "NotificationService v1");
    c.SwaggerEndpoint("/m01/swagger/v1/swagger.json", "M01Service v1");
});

app.MapGet("/", () => Results.Ok(new
{
    name = "Hdos API Gateway",
    routes = new[] { "/auth/*", "/orders/*", "/notifications/*", "/m01/*", "/health", "/swagger" }
})).AllowAnonymous();

app.UseHdosMonitoring();

app.MapGet("/health", () => Results.Ok(new { status = "OK", service = "ApiGateway" }))
    .AllowAnonymous();

app.MapReverseProxy()
    .RequireCors(Hdos.Common.Extensions.ServiceCollectionExtensions.HdosCorsPolicy);

app.Run();
