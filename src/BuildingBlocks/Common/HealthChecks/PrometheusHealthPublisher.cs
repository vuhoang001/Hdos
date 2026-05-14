using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;

namespace Hdos.Common.HealthChecks;

internal sealed class PrometheusHealthPublisher : IHealthCheckPublisher
{
    private static readonly Gauge HealthCheckStatus = Metrics.CreateGauge(
        "hdos_health_check_status",
        "Health check status per dependency: 1 = Healthy, 0 = Unhealthy/Degraded",
        new GaugeConfiguration { LabelNames = ["check"] });

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        foreach (var (name, entry) in report.Entries)
            HealthCheckStatus.WithLabels(name).Set(entry.Status == HealthStatus.Healthy ? 1 : 0);

        return Task.CompletedTask;
    }
}
