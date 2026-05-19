using Hdos.AuthService.API.Grpc;
using Hdos.AuthService.Application;
using Hdos.AuthService.Infrastructure;
using Hdos.AuthService.Infrastructure.Persistence;
using Hdos.Common.Auth;
using Hdos.Common.Extensions;
using Hdos.Common.HealthChecks;
using Hdos.Common.Logging;
using Hdos.Common.Monitoring;
using Hdos.Common.Swagger;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.UseHdosLogging("AuthService");

// Two listeners: REST on RestPort (HTTP/1.1+HTTP/2), gRPC on GrpcPort (HTTP/2 only).
// Splitting them keeps Swashbuckle (HTTP/1.1) happy while gRPC needs HTTP/2 over plaintext.
builder.WebHost.ConfigureKestrel(options =>
{
    var restPort = builder.Configuration.GetValue<int>("Kestrel:RestPort", 8080);
    var grpcPort = builder.Configuration.GetValue<int>("Kestrel:GrpcPort", 8081);
    options.ListenAnyIP(restPort, lo => lo.Protocols = HttpProtocols.Http1AndHttp2);
    options.ListenAnyIP(grpcPort, lo => lo.Protocols = HttpProtocols.Http2);
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();
builder.Services.AddHdosSwagger("AuthService", builder.Configuration["Keycloak:PublicAuthority"]);
builder.Services.AddGrpc();

builder.Services.AddAuthApplication();
builder.Services.AddAuthInfrastructure(builder.Configuration);
builder.Services.AddHdosJwtAuth(builder.Configuration);
builder.Services.AddHdosAuthorization();
builder.Services.AddHdosCors(builder.Configuration);

builder.Services.AddHdosOpenTelemetry(builder.Configuration, "AuthService");
builder.Services.AddHdosHealthChecks(builder.Configuration, sqlConnectionStringKey: "AuthDb", checkRabbitMq: true);

var app = builder.Build();

// Path khớp với prefix YARP forward (`/auth/...`) để Gateway có thể tổng hợp.
app.UseHdosSwaggerUI("auth", "AuthService v1");

app.UseHdosMiddleware();
app.UseHdosCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGrpcService<UserGrpcService>();
app.UseHdosMonitoring();

await EnsureDatabaseAsync(app);

app.Run();

static async Task EnsureDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var attempts = 0;
    while (attempts < 10)
    {
        try { await db.Database.MigrateAsync(); return; }
        catch (Exception ex)
        {
            attempts++;
            logger.LogWarning(ex, "Auth DB migration retry {Attempt}/10", attempts);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}
