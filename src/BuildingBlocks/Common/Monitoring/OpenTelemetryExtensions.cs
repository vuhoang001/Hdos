using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Hdos.Common.Monitoring;

public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddHdosOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        var environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? "production";
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        // 1.0 = sample everything (dev default). Lower for high-traffic production (e.g., 0.1 = 10%).
        var samplingRatio = configuration.GetValue<double>("OpenTelemetry:SamplingRatio", 1.0);

        if (string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[WARN][{serviceName}] OpenTelemetry:OtlpEndpoint is not configured — traces will not be exported. " +
                              "Set it to http://localhost:4317 (local) or http://tempo:4317 (Docker) to enable tracing.");
            Console.ResetColor();
        }

        // Traces only — metrics handled by prometheus-net (avoids OTel pre-release dependency conflicts)
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName, serviceVersion: "1.0.0")
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = environment
                }))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio)))
                    .AddAspNetCoreInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                        opts.Filter = ctx =>
                            !ctx.Request.Path.StartsWithSegments("/health") &&
                            !ctx.Request.Path.StartsWithSegments("/metrics");
                    })
                    .AddHttpClientInstrumentation(opts => opts.RecordException = true)
                    .AddSource("Hdos.Messaging");

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
            });

        return services;
    }
}
