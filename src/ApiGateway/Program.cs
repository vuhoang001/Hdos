using Hdos.Common.Auth;
using Hdos.Common.Extensions;
using Hdos.Common.HealthChecks;
using Hdos.Common.Logging;
using Hdos.Common.Monitoring;
using Hdos.Common.Swagger;

var builder = WebApplication.CreateBuilder(args);
builder.UseHdosLogging("AsyncGateway");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHdosSwagger("AsyncGateway");

builder.Services.AddMassTransitMessaging(builder.Configuration);
builder.Services.AddHdosJwtAuth(builder.Configuration);
builder.Services.AddHdosAuthorization();
builder.Services.AddHdosCors(builder.Configuration);

builder.Services.AddHdosOpenTelemetry(builder.Configuration, "AsyncGateway");
builder.Services.AddHdosHealthChecks(builder.Configuration, checkRabbitMq: true);

var app = builder.Build();

app.UseSwagger(c => c.RouteTemplate = "async/swagger/{documentName}/swagger.json");
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/async/swagger/v1/swagger.json", "AsyncGateway v1");
    c.RoutePrefix = "async/swagger";
});

app.UseHdosMiddleware();
app.UseHdosCors();
app.UseAuthentication();
app.UseHdosPermissions();
app.UseAuthorization();
app.MapControllers();
app.UseHdosMonitoring();

app.Run();
