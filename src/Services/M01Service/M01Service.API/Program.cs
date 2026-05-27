using Hdos.Common.Auth;
using Hdos.Common.Extensions;
using Hdos.Common.HealthChecks;
using Hdos.Common.Logging;
using Hdos.Common.Monitoring;
using Hdos.Common.Swagger;
using Hdos.M01Service.Application;
using Hdos.M01Service.Infrastructure;
using Hdos.M01Service.Infrastructure.Persistence;
using Hdos.M01Service.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.UseHdosLogging("M01Service");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHdosSwagger("M01Service");

builder.Services.AddM01Application();
builder.Services.AddM01Infrastructure(builder.Configuration);
builder.Services.AddHdosJwtAuth(builder.Configuration);
builder.Services.AddHdosAuthorization();
builder.Services.AddHdosCors(builder.Configuration);

builder.Services.AddHdosOpenTelemetry(builder.Configuration, "M01Service");
builder.Services.AddHdosHealthChecks(builder.Configuration, sqlConnectionStringKey: "M01Db");

var app = builder.Build();

app.UseHdosSwaggerUi("m01", "M01Service v1");

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
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<M01DbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var attempts = 0;
    while (attempts < 10)
    {
        try
        {
            // If DB exists but has no __EFMigrationsHistory (was created with EnsureCreated),
            // drop it so MigrateAsync can build the correct schema including outbox tables.
            if (await db.Database.CanConnectAsync())
            {
                var applied = await db.Database.GetAppliedMigrationsAsync();
                if (!applied.Any())
                    await db.Database.EnsureDeletedAsync();
            }
            await db.Database.MigrateAsync();
            await M01Seeder.SeedAsync(db);
            return;
        }
        catch (Exception ex)
        {
            attempts++;
            logger.LogWarning(ex, "M01 DB bootstrap retry {Attempt}/10", attempts);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}
