using Hdos.Common.Auth;
using Hdos.Common.Extensions;
using Hdos.Common.HealthChecks;
using Hdos.Common.Logging;
using Hdos.Common.Monitoring;
using Hdos.Common.Swagger;
using Hdos.LakehouseService.Application;
using Hdos.LakehouseService.Infrastructure;
using Hdos.LakehouseService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.UseHdosLogging("LakehouseService");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHdosSwagger("LakehouseService");

builder.Services.AddLakehouseApplication();
builder.Services.AddLakehouseInfrastructure(builder.Configuration);
builder.Services.AddHdosJwtAuth(builder.Configuration);
builder.Services.AddHdosAuthorization();
builder.Services.AddHdosCors(builder.Configuration);

builder.Services.AddHdosOpenTelemetry(builder.Configuration, "LakehouseService");
builder.Services.AddHdosHealthChecks(builder.Configuration, checkRabbitMq: true);
builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("LakehouseDb")!,
        name: "postgres",
        tags: ["db"]);

var app = builder.Build();

app.UseHdosSwaggerUi("lakehouse", "LakehouseService v1");

app.UseHdosMiddleware();
app.UseHdosCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.UseHdosMonitoring();

await EnsureDatabaseAsync(app);

app.Run();

static async Task EnsureDatabaseAsync(WebApplication app)
{
    using var scope    = app.Services.CreateScope();
    var       db       = scope.ServiceProvider.GetRequiredService<LakehouseDbContext>();
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
            logger.LogWarning(ex, "Lakehouse DB migration retry {Attempt}/10", attempts);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}
