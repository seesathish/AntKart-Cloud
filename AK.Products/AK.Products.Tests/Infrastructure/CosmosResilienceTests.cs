using System.Net;
using AK.BuildingBlocks.Resilience;
using AK.Products.Infrastructure.Resilience;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using Polly;

namespace AK.Products.Tests.Infrastructure;

public sealed class CosmosResilienceTests
{
    // Builds a MongoCommandException that mirrors what Cosmos DB for MongoDB returns: an error
    // document carrying a server "code" (and, for a 429, a "RetryAfterMs" hint).
    private static MongoCommandException CommandException(int code, BsonDocument? extra = null)
    {
        var result = new BsonDocument { { "ok", 0.0 }, { "code", code }, { "errmsg", "test" } };
        if (extra is not null) result.Merge(extra, overwriteExistingElements: true);

        var connectionId = new ConnectionId(
            new ServerId(new ClusterId(1), new DnsEndPoint("localhost", 27017)));
        return new MongoCommandException(connectionId, "command failed", new BsonDocument(), result);
    }

    [Fact]
    public void IsTransient_429Throttle_ReturnsTrue() =>
        CosmosResilience.IsTransient(CommandException(CosmosResilience.TooManyRequests))
            .Should().BeTrue();

    [Fact]
    public void IsTransient_DuplicateKey_ReturnsFalse() =>
        CosmosResilience.IsTransient(CommandException(11000)).Should().BeFalse();

    [Fact]
    public void IsTransient_TimeoutException_ReturnsTrue() =>
        CosmosResilience.IsTransient(new TimeoutException()).Should().BeTrue();

    [Fact]
    public void GetRetryAfter_429WithHint_ReturnsServerDelay()
    {
        var ex = CommandException(CosmosResilience.TooManyRequests,
            new BsonDocument { { "RetryAfterMs", 1500 } });

        CosmosResilience.GetRetryAfter(ex).Should().Be(TimeSpan.FromMilliseconds(1500));
    }

    [Fact]
    public void GetRetryAfter_429WithoutHint_ReturnsNull() =>
        CosmosResilience.GetRetryAfter(CommandException(CosmosResilience.TooManyRequests))
            .Should().BeNull();

    [Fact]
    public void GetRetryAfter_NonThrottleError_ReturnsNull() =>
        CosmosResilience.GetRetryAfter(CommandException(11000)).Should().BeNull();

    private static ResiliencePipeline BuildPipeline(int maxRetryAttempts) =>
        new ResiliencePipelineBuilder()
            .AddDataStoreRetry(
                CosmosResilience.IsTransient, CosmosResilience.GetRetryAfter,
                maxRetryAttempts: maxRetryAttempts, baseDelay: TimeSpan.FromMilliseconds(1))
            .Build();

    [Fact]
    public async Task Pipeline_RetriesTransientFault_ThenSucceeds()
    {
        var pipeline = BuildPipeline(maxRetryAttempts: 3);

        var attempts = 0;
        var result = await pipeline.ExecuteAsync(async _ =>
        {
            attempts++;
            if (attempts < 3)
                throw CommandException(CosmosResilience.TooManyRequests,
                    new BsonDocument { { "RetryAfterMs", 1 } });
            await Task.CompletedTask;
            return "ok";
        });

        result.Should().Be("ok");
        attempts.Should().Be(3); // 2 retries + the succeeding attempt
    }

    [Fact]
    public async Task Pipeline_DoesNotRetry_NonTransientFault()
    {
        var pipeline = BuildPipeline(maxRetryAttempts: 3);

        var attempts = 0;
        Func<CancellationToken, ValueTask<string>> op = async _ =>
        {
            attempts++;
            await Task.CompletedTask;
            throw CommandException(11000); // duplicate key — a logical error, never retried
        };

        var act = async () => await pipeline.ExecuteAsync(op);

        await act.Should().ThrowAsync<MongoCommandException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task Pipeline_HonoursServerRetryAfter_NotTheBaseDelay()
    {
        // The 429 carries a 200ms Retry-After. With a 1ms base delay, a wait of ~200ms proves the
        // SERVER hint governs the backoff — exactly the Cosmos-throttling requirement.
        var pipeline = BuildPipeline(maxRetryAttempts: 1);

        var attempts = 0;
        Func<CancellationToken, ValueTask<string>> op = async _ =>
        {
            attempts++;
            await Task.CompletedTask;
            throw CommandException(CosmosResilience.TooManyRequests,
                new BsonDocument { { "RetryAfterMs", 200 } });
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var act = async () => await pipeline.ExecuteAsync(op);
        await act.Should().ThrowAsync<MongoCommandException>();
        sw.Stop();

        attempts.Should().Be(2); // initial attempt + 1 retry
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(150);
    }
}
