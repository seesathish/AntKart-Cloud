using System.Diagnostics;
using AK.BuildingBlocks.Logging;
using FluentAssertions;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace AK.BuildingBlocks.Tests.Logging;

public sealed class ActivityEnricherTests
{
    private static readonly MessageTemplate Template = new MessageTemplateParser().Parse("test");

    private sealed class Factory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
            => new(name, new ScalarValue(value));
    }

    private static LogEvent NewEvent() => new(
        DateTimeOffset.Now, LogEventLevel.Information, exception: null,
        Template, Enumerable.Empty<LogEventProperty>());

    [Fact]
    public void Enrich_WithActivity_AddsTraceIdAndSpanId()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("ActivityEnricherTests.Source");
        using var activity = source.StartActivity("op");
        activity.Should().NotBeNull();

        var logEvent = NewEvent();
        new ActivityEnricher().Enrich(logEvent, new Factory());

        ((ScalarValue)logEvent.Properties["TraceId"]).Value.Should().Be(activity!.TraceId.ToString());
        ((ScalarValue)logEvent.Properties["SpanId"]).Value.Should().Be(activity.SpanId.ToString());
    }

    [Fact]
    public void Enrich_WithoutActivity_IsNoOp()
    {
        Activity.Current = null; // no ambient activity in scope

        var logEvent = NewEvent();
        new ActivityEnricher().Enrich(logEvent, new Factory());

        logEvent.Properties.Should().NotContainKey("TraceId");
        logEvent.Properties.Should().NotContainKey("SpanId");
    }
}
