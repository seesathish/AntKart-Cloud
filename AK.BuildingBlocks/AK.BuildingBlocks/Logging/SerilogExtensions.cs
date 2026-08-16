using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace AK.BuildingBlocks.Logging;

public static class SerilogExtensions
{
    // Structured logging via Serilog. Every event is enriched with ServiceName, Environment and
    // (via LogContext) CorrelationId. The console sink is environment-conditional:
    //   - Development: a human-readable template (incl. CorrelationId) for local debugging.
    //   - Cloud (Staging/Production): RenderedCompactJsonFormatter emits ONE JSON object per event,
    //     so EVERY enriched property (ServiceName, Environment, CorrelationId, ...) survives into
    //     Application Insights / Log Analytics as a queryable field, and multi-line exceptions stay
    //     a single event. The console stream is what the platform collects; this method does the
    //     shaping, not Application Insights.
    // The rolling-file sink stays on the human-readable default — it is a local dev convenience only.
    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
    {
        var serviceName = builder.Environment.ApplicationName;
        var environment = builder.Environment.EnvironmentName;

        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Grpc", LogEventLevel.Warning)
            .MinimumLevel.Override("MassTransit", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", serviceName)
            .Enrich.WithProperty("Environment", environment)
            // Adds TraceId/SpanId from the ambient Activity so logs can be pivoted to their trace.
            .Enrich.With(new ActivityEnricher());

        if (builder.Environment.IsDevelopment())
            loggerConfiguration.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] [{CorrelationId}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
        else
            loggerConfiguration.WriteTo.Console(new RenderedCompactJsonFormatter());

        Log.Logger = loggerConfiguration
            .WriteTo.File($"logs/{serviceName}-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        builder.Host.UseSerilog();
        return builder;
    }
}
