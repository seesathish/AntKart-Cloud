using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace AK.BuildingBlocks.Resilience;

// Polly v8 resilience pipelines shared across all services.
// These wrap outbound calls (HTTP, Redis, PostgreSQL) with retry + circuit breaker logic
// so a temporary infrastructure hiccup doesn't immediately fail a user request.
public static class ResilienceExtensions
{
    // Attaches a Retry → Circuit Breaker → Timeout pipeline to an HttpClient.
    // Used for: Razorpay REST calls, Discount gRPC client, and other outbound HTTP dependencies.
    //
    // How the pipeline works (layers execute in the order listed):
    //   1. Retry:          on transient failure, wait 300ms × 2^n + jitter and try again (up to 3 times)
    //   2. Circuit Breaker: if >50% of the last 5+ requests fail within 60s, stop trying for 30s
    //                       (half-open after break: lets one test request through to check recovery)
    //   3. Timeout:        if a single attempt takes >15s, cancel it (counted as a failure for CB)
    //
    // UseJitter adds a random offset to retry delays so many instances don't all retry simultaneously
    // (thundering herd prevention).
    public static IHttpClientBuilder AddHttpResilienceWithCircuitBreaker(
        this IHttpClientBuilder builder,
        int maxRetryAttempts = 3,
        double failureRatio = 0.5,
        int minimumThroughput = 5,
        int breakDurationSeconds = 30)
    {
        builder.AddResilienceHandler("circuit-breaker", pipeline =>
        {
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(300),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            });

            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = failureRatio,
                MinimumThroughput = minimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(60),
                BreakDuration = TimeSpan.FromSeconds(breakDurationSeconds)
            });

            pipeline.AddTimeout(TimeSpan.FromSeconds(15));
        });
        return builder;
    }

    // FAIL-FAST resilience for an OPTIONAL dependency (e.g. price enrichment, recommendations) —
    // a service the caller can do without. This is the deliberate CONTRAST to the *patient*
    // resilience above (and to Cosmos/Service Bus): a CRITICAL dependency is worth waiting and
    // retrying for, an OPTIONAL one is NOT — it must never slow or fail the core request.
    //
    // The differences from the patient pipeline:
    //   * NO retry. Retrying a down optional service only MULTIPLIES the user-facing latency for a
    //     result that is, by definition, dispensable.
    //   * A circuit breaker that OPENS QUICKLY (after a couple of consecutive failures) and stays
    //     open for a cooldown. Once the dependency is known-down, every subsequent call
    //     SHORT-CIRCUITS instantly (microseconds) instead of waiting for another timeout — so a
    //     page of items doesn't pay the timeout once per item.
    //   * A SHORT per-call timeout, so a slow/hung optional call cannot drag the request out.
    // The caller also sets a short HttpClient.Timeout as the hard per-call ceiling, and treats any
    // failure as "no data" (returns null / degrades silently).
    public static IHttpClientBuilder AddOptionalDependencyResilience(
        this IHttpClientBuilder builder,
        int minimumThroughput = 2,
        int samplingDurationSeconds = 10,
        int breakDurationSeconds = 30,
        double attemptTimeoutSeconds = 2)
    {
        builder.AddResilienceHandler("optional-fail-fast", pipeline =>
        {
            // Circuit breaker FIRST (outermost): once open it throws immediately, so a known-down
            // dependency is skipped without even starting a call or a timeout.
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                // 0.9 over a small throughput window ⇒ "(almost) all of the last few calls failed".
                FailureRatio = 0.9,
                MinimumThroughput = minimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(samplingDurationSeconds),
                BreakDuration = TimeSpan.FromSeconds(breakDurationSeconds)
            });

            // Short per-call timeout. A Polly timeout surfaces as TimeoutRejectedException, which the
            // HTTP circuit breaker counts as a failure — so repeated slow calls open the breaker too.
            pipeline.AddTimeout(TimeSpan.FromSeconds(attemptTimeoutSeconds));
        });
        return builder;
    }

    // Retry pipeline for Redis operations (cart reads/writes).
    // Short timeout (5s) because a slow Redis response usually means connection issues.
    public static IServiceCollection AddRedisResilience(this IServiceCollection services)
    {
        services.AddResiliencePipeline("redis", pipeline =>
        {
            pipeline.AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            });

            pipeline.AddTimeout(TimeSpan.FromSeconds(5));
        });

        return services;
    }

    // Retry pipeline for PostgreSQL (Npgsql) operations (orders, payments, notifications).
    // Longer timeout (30s) because DB queries can legitimately take a few seconds under load.
    // Exponential backoff + jitter prevents all services from hammering the DB at once
    // after it recovers from a brief outage.
    public static IServiceCollection AddNpgsqlResilience(this IServiceCollection services)
    {
        services.AddResiliencePipeline("npgsql", pipeline =>
        {
            pipeline.AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            });

            pipeline.AddTimeout(TimeSpan.FromSeconds(30));
        });

        return services;
    }

    // ── Cloud data-store retry that HONOURS a server-supplied Retry-After ────────────────────────
    //
    // Builds a retry pipeline for a throttling cloud data store (e.g. Azure Cosmos DB). It is the
    // reusable mechanism; the data-store SPECIFICS are supplied by the caller as two delegates, so
    // this shared library carries NO data-store driver dependency (no MongoDB.Driver here — the
    // Cosmos specifics live in AK.Products.Infrastructure).
    //
    // WHY retries belong HERE, at the data-access call site — not in the driver or a global filter:
    //   • The call site owns the operation's idempotency and its CancellationToken, so it can retry
    //     SAFELY and abort promptly. A blind, transport-level retry can replay a non-idempotent
    //     write or mask a genuine outage.
    //
    // WHY Cosmos throttling MUST respect Retry-After rather than retrying blindly:
    //   • Cosmos enforces a provisioned-throughput (RU) budget. Exceed it and the request is
    //     rejected with 429 ("request rate too large") AND a Retry-After hint saying HOW LONG to
    //     wait. Retrying BEFORE that window elapses spends more of the budget and DEEPENS the
    //     throttling — a self-inflicted outage. Honour the server's hint; only when there is no
    //     hint do we fall back to our own exponential backoff.
    //
    //   isTransient    — which exceptions are worth retrying (429 / timeout / dropped connection);
    //   getRetryAfter  — pull the server's Retry-After out of the exception (null when absent).
    public static ResiliencePipelineBuilder AddDataStoreRetry(
        this ResiliencePipelineBuilder builder,
        Func<Exception, bool> isTransient,
        Func<Exception, TimeSpan?> getRetryAfter,
        int maxRetryAttempts = 5,
        TimeSpan? baseDelay = null,
        TimeSpan? attemptTimeout = null)
    {
        builder.AddRetry(new Polly.Retry.RetryStrategyOptions
        {
            ShouldHandle = args => new ValueTask<bool>(
                args.Outcome.Exception is { } ex && isTransient(ex)),
            MaxRetryAttempts = maxRetryAttempts,

            // Fallback schedule when the server gives NO Retry-After: exponential backoff with
            // jitter. Jitter de-synchronises many instances so they don't all retry on the same
            // tick (thundering-herd prevention).
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = baseDelay ?? TimeSpan.FromMilliseconds(200),

            // DelayGenerator runs before each retry's wait. If the server told us exactly how long
            // to wait (429 Retry-After), return THAT and Polly uses it verbatim (no jitter added on
            // top). Returning null hands control back to the exponential-backoff schedule above.
            DelayGenerator = args =>
            {
                var retryAfter = args.Outcome.Exception is { } ex ? getRetryAfter(ex) : null;
                return new ValueTask<TimeSpan?>(retryAfter);
            }
        });

        // Per-attempt ceiling so one wedged call cannot hang a request indefinitely. Added AFTER
        // the retry strategy, so it wraps each individual attempt (the retry strategy is outer).
        builder.AddTimeout(attemptTimeout ?? TimeSpan.FromSeconds(20));
        return builder;
    }

    // Registers a named data-store retry pipeline (built by AddDataStoreRetry) in DI, resolvable
    // via ResiliencePipelineProvider<string>.GetPipeline(key). The caller supplies the data-store
    // specifics (isTransient / getRetryAfter); this keeps the Polly registration in one place so
    // each service just names its store, e.g. AddDataStoreResiliencePipeline("cosmos", ...).
    public static IServiceCollection AddDataStoreResiliencePipeline(
        this IServiceCollection services,
        string key,
        Func<Exception, bool> isTransient,
        Func<Exception, TimeSpan?> getRetryAfter,
        int maxRetryAttempts = 5,
        TimeSpan? baseDelay = null,
        TimeSpan? attemptTimeout = null)
    {
        services.AddResiliencePipeline(key, builder =>
            builder.AddDataStoreRetry(
                isTransient, getRetryAfter, maxRetryAttempts, baseDelay, attemptTimeout));
        return services;
    }
}
