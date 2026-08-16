using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace AK.BuildingBlocks.Logging;

// Adds TraceId and SpanId from the ambient System.Diagnostics.Activity to every log event, so a
// slow span in Application Insights can be pivoted to the log lines for that same operation (and
// back). OpenTelemetry sets the W3C id format, so these are the same trace/span ids the exporter
// reports. A no-op when no activity is in scope (Activity.Current is null).
public sealed class ActivityEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
            return;

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
    }
}
