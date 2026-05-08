using Microsoft.AspNetCore.Builder;
using Serilog;
using Serilog.Sinks.Grafana.Loki;

namespace Hdos.Common.Logging;

public static class SerilogConfig
{
    public static WebApplicationBuilder UseHdosLogging(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Host.UseSerilog((ctx, lc) =>
        {
            lc.Enrich.FromLogContext()
              .Enrich.WithProperty("Service", serviceName)
              .ReadFrom.Configuration(ctx.Configuration)
              .WriteTo.Console(outputTemplate:
                  "[{Timestamp:HH:mm:ss} {Level:u3}] [{Service}] {Message:lj} {Properties:j}{NewLine}{Exception}");

            var lokiUri = ctx.Configuration["Loki:Uri"];
            if (!string.IsNullOrWhiteSpace(lokiUri))
            {
                lc.WriteTo.GrafanaLoki(
                    lokiUri,
                    labels:
                    [
                        new LokiLabel { Key = "service", Value = serviceName },
                        new LokiLabel { Key = "environment", Value = ctx.HostingEnvironment.EnvironmentName }
                    ],
                    propertiesAsLabels: ["level"]);
            }
        });
        return builder;
    }
}
