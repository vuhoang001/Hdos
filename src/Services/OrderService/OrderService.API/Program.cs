using Hdos.Common.Auth;
using Hdos.Common.Extensions;
using Hdos.Common.Logging;
using Hdos.Common.Swagger;
using Hdos.OrderService.Application;
using Hdos.OrderService.Infrastructure;
using Hdos.OrderService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

// Required so the gRPC client can speak HTTP/2 over plain http:// to AuthService.
// Without TLS, .NET refuses HTTP/2 by default.
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);
builder.UseHdosLogging("OrderService");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHdosSwagger("OrderService");

builder.Services.AddOrderApplication();
builder.Services.AddOrderInfrastructure(builder.Configuration);
builder.Services.AddHdosJwtAuth(builder.Configuration);
builder.Services.AddHdosCors();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger(c => c.RouteTemplate = "orders/swagger/{documentName}/swagger.json");
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/orders/swagger/v1/swagger.json", "OrderService v1");
        c.RoutePrefix = "orders/swagger";
    });
}

app.UseHdosMiddleware();
app.UseHdosCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await EnsureDatabaseAsync(app);

app.Run();

static async Task EnsureDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var attempts = 0;
    while (attempts < 10)
    {
        try { await db.Database.MigrateAsync(); return; }
        catch (Exception ex)
        {
            attempts++;
            logger.LogWarning(ex, "Order DB migration retry {Attempt}/10", attempts);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}
