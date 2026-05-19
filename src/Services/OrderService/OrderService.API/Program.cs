using Hdos.Common.Auth;
using Hdos.Common.Extensions;
using Hdos.Common.HealthChecks;
using Hdos.Common.Logging;
using Hdos.Common.Monitoring;
using Hdos.Common.Swagger;
using Hdos.OrderService.Application;
using Hdos.OrderService.Infrastructure;
using Hdos.OrderService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);
builder.UseHdosLogging("OrderService");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHdosSwagger("OrderService", builder.Configuration["Keycloak:Authority"]);

builder.Services.AddOrderApplication();
builder.Services.AddOrderInfrastructure(builder.Configuration);
builder.Services.AddHdosJwtAuth(builder.Configuration);
builder.Services.AddHdosAuthorization();
builder.Services.AddHdosCors(builder.Configuration);

builder.Services.AddHdosOpenTelemetry(builder.Configuration, "OrderService");
builder.Services.AddHdosHealthChecks(builder.Configuration, sqlConnectionStringKey: "OrderDb", checkRabbitMq: true);

var app = builder.Build();

app.UseHdosSwaggerUI("orders", "OrderService v1");

app.UseHdosMiddleware();
app.UseHdosCors();
app.UseAuthentication();
app.UseHdosPermissions();
app.UseAuthorization();
app.MapControllers();
app.UseHdosMonitoring();

await EnsureDatabaseAsync(app);

app.Run();

static async Task EnsureDatabaseAsync(WebApplication app)
{
    using var scope    = app.Services.CreateScope();
    var       db       = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    var       logger   = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var       attempts = 0;
    while (attempts < 10)
    {
        try
        {
            await db.Database.MigrateAsync();
            return;
        }
        catch (Exception ex)
        {
            attempts++;
            logger.LogWarning(ex, "Order DB migration retry {Attempt}/10", attempts);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}