using Hdos.AuthService.API.Grpc;
using Hdos.AuthService.Application;
using Hdos.AuthService.Infrastructure;
using Hdos.AuthService.Infrastructure.Persistence;
using Hdos.Common.Auth;
using Hdos.Common.Extensions;
using Hdos.Common.Logging;
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
builder.Services.AddSwaggerGen();
builder.Services.AddGrpc();

builder.Services.AddAuthApplication();
builder.Services.AddAuthInfrastructure(builder.Configuration);
builder.Services.AddHdosJwtAuth(builder.Configuration);
builder.Services.AddHdosJwtIssuer(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Path khớp với prefix YARP forward (`/auth/...`) để Gateway có thể tổng hợp.
    app.UseSwagger(c => c.RouteTemplate = "auth/swagger/{documentName}/swagger.json");
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/auth/swagger/v1/swagger.json", "AuthService v1");
        c.RoutePrefix = "auth/swagger";
    });
}

app.UseHdosMiddleware();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGrpcService<UserGrpcService>();

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
