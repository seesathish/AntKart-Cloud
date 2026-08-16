using AK.BuildingBlocks.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace AK.BuildingBlocks.Tests.Middleware;

public sealed class CorrelationIdMiddlewareTests
{
    private const string Header = "X-Correlation-Id";

    [Fact]
    public async Task InvokeAsync_NoIncomingHeader_PopulatesRequestHeaderAndEchoesInResponse()
    {
        var context = new DefaultHttpContext();
        string? requestHeaderSeenByDownstream = null;

        var middleware = new CorrelationIdMiddleware(ctx =>
        {
            // Capture what a downstream forward (Ocelot) would see on the request.
            requestHeaderSeenByDownstream = ctx.Request.Headers[Header].ToString();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        var generated = context.Request.Headers[Header].ToString();
        generated.Should().NotBeNullOrEmpty();
        Guid.TryParse(generated, out _).Should().BeTrue("a generated correlation id is a GUID");

        // The generated id is on the request BEFORE next runs, so it propagates downstream.
        requestHeaderSeenByDownstream.Should().Be(generated);
        // And the same id is echoed back in the response.
        context.Response.Headers[Header].ToString().Should().Be(generated);
    }

    [Fact]
    public async Task InvokeAsync_IncomingHeader_IsReusedAndNeverReplaced()
    {
        const string incoming = "abc-123";
        var context = new DefaultHttpContext();
        context.Request.Headers[Header] = incoming;

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        // The caller's id is left untouched on the request and echoed in the response.
        context.Request.Headers[Header].ToString().Should().Be(incoming);
        context.Response.Headers[Header].ToString().Should().Be(incoming);
    }
}
