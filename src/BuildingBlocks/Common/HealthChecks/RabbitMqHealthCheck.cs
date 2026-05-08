using Hdos.Common.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hdos.Common.HealthChecks;

internal sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly RabbitMqConnection _connection;

    public RabbitMqHealthCheck(RabbitMqConnection connection) => _connection = connection;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _connection.EnsureConnected();
            return Task.FromResult(HealthCheckResult.Healthy("RabbitMQ connected"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ connection failed", ex));
        }
    }
}
