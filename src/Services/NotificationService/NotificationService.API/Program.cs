using Hdos.Common.Auth;
using Hdos.Common.Extensions;
using Hdos.Common.HealthChecks;
using Hdos.Common.Logging;
using Hdos.Common.Monitoring;
using Hdos.Common.Swagger;
using Hdos.NotificationService.API.Hubs;
using Hdos.NotificationService.Application;
using Hdos.NotificationService.Application.Realtime;
using Hdos.NotificationService.Infrastructure;
using Hdos.NotificationService.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.UseHdosLogging("NotificationService");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHdosSwagger("NotificationService");

builder.Services.AddNotificationApplication();
builder.Services.AddNotificationInfrastructure(builder.Configuration);
builder.Services.AddHdosJwtAuth(builder.Configuration);
builder.Services.AddHdosCors();

builder.Services.AddHdosOpenTelemetry(builder.Configuration, "NotificationService");
builder.Services.AddHdosHealthChecks(builder.Configuration, sqlConnectionStringKey: "NotificationDb", checkRabbitMq: true);

builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, EmailUserIdProvider>();
builder.Services.AddScoped<INotificationPusher, SignalRNotificationPusher>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger(c => c.RouteTemplate = "notifications/swagger/{documentName}/swagger.json");
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/notifications/swagger/v1/swagger.json", "NotificationService v1");
        c.RoutePrefix = "notifications/swagger";
    });
}

app.UseHdosMiddleware();
app.UseHdosCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<NotificationHub>("/notifications/hubs/notifications");
app.UseHdosMonitoring();

await EnsureDatabaseAsync(app);

app.Run();

static async Task EnsureDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var attempts = 0;
    while (attempts < 10)
    {
        try { await db.Database.MigrateAsync(); return; }
        catch (Exception ex)
        {
            attempts++;
            logger.LogWarning(ex, "Notification DB migration retry {Attempt}/10", attempts);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}
