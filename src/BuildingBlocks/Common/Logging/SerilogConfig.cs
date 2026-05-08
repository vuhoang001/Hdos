using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Hdos.Common.Logging;

public static class SerilogConfig
{
    public static WebApplicationBuilder UseHdosLogging(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Host.UseSerilog((ctx, lc) => lc
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .ReadFrom.Configuration(ctx.Configuration)
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] [{Service}] {Message:lj} {Properties:j}{NewLine}{Exception}"));
        return builder;
    }
}
