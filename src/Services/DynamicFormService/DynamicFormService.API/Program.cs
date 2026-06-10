using Hdos.Common.Auth;
using Hdos.Common.Extensions;
using Hdos.Common.HealthChecks;
using Hdos.Common.Logging;
using Hdos.Common.Monitoring;
using Hdos.Common.Swagger;
using Hdos.DynamicFormService.API.Grpc;
using Hdos.DynamicFormService.Application;
using Hdos.DynamicFormService.Infrastructure;
using Hdos.DynamicFormService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.UseHdosLogging("DynamicFormService");

// REST trên RestPort (HTTP/1.1+HTTP/2 cho Swagger), gRPC trên GrpcPort (HTTP/2 only).
builder.WebHost.ConfigureKestrel(options =>
{
    var restPort = builder.Configuration.GetValue<int>("Kestrel:RestPort", 8080);
    var grpcPort = builder.Configuration.GetValue<int>("Kestrel:GrpcPort", 8081);
    options.ListenAnyIP(restPort, lo => lo.Protocols = HttpProtocols.Http1AndHttp2);
    options.ListenAnyIP(grpcPort, lo => lo.Protocols = HttpProtocols.Http2);
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHdosSwagger("DynamicFormService");
builder.Services.AddGrpc();

builder.Services.AddDynamicFormApplication();
builder.Services.AddDynamicFormInfrastructure(builder.Configuration);
builder.Services.AddHdosJwtAuth(builder.Configuration);
builder.Services.AddHdosAuthorization();
builder.Services.AddHdosCors(builder.Configuration);

builder.Services.AddHdosOpenTelemetry(builder.Configuration, "DynamicFormService");
builder.Services.AddHdosHealthChecks(builder.Configuration, checkRabbitMq: true);
builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("DynamicFormDb")!,
        name: "postgres",
        tags: ["db"]);

var app = builder.Build();

app.UseHdosSwaggerUi("forms", "DynamicFormService v1");

app.UseHdosMiddleware();
app.UseHdosCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGrpcService<DataContractRegistrySyncGrpcService>();
app.UseHdosMonitoring();

await EnsureDatabaseAsync(app);

app.Run();

static async Task EnsureDatabaseAsync(WebApplication app)
{
    using var scope    = app.Services.CreateScope();
    var       db       = scope.ServiceProvider.GetRequiredService<DynamicFormDbContext>();
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
            logger.LogWarning(ex, "DynamicForm DB migration retry {Attempt}/10", attempts);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}
