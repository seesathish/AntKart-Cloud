using MongoDB.Driver;

namespace AK.Products.Infrastructure.Resilience;

// Cosmos DB (MongoDB API)-SPECIFIC transient-fault knowledge. Kept OUT of AK.BuildingBlocks so the
// shared library carries no MongoDB.Driver dependency: these two delegates are paired with the
// generic AddDataStoreRetry pipeline in BuildingBlocks (which owns the retry/backoff mechanism).
public static class CosmosResilience
{
    // Cosmos DB for MongoDB surfaces "request rate too large" (429) as a MongoCommandException with
    // this server error code, and embeds a Retry-After hint (milliseconds) on the error document.
    public const int TooManyRequests = 16500;   // 429 — provisioned throughput (RU) exceeded
    public const int ExceededTimeLimit = 50;     // operation exceeded its server time limit

    // Which failures are worth retrying. Throttling (429) and timeouts are transient by definition;
    // connection-level faults (a dropped/replaced connection, a pool timeout) are too. A LOGICAL
    // error (bad query, duplicate key) is NOT retried — replaying it would fail identically.
    public static bool IsTransient(Exception ex) => ex switch
    {
        MongoCommandException command => command.Code is TooManyRequests or ExceededTimeLimit,
        MongoConnectionException => true,
        MongoExecutionTimeoutException => true,
        TimeoutException => true,
        _ => false
    };

    // Extract Cosmos's Retry-After hint from a 429. The MongoDB-API response embeds the wait as
    // "RetryAfterMs" on the error document. Honouring it (instead of our own backoff) is exactly
    // what stops a throttled client from hammering the store and prolonging the throttling.
    public static TimeSpan? GetRetryAfter(Exception ex)
    {
        if (ex is not MongoCommandException command || command.Code != TooManyRequests)
            return null;

        var result = command.Result;
        if (result is not null &&
            result.TryGetValue("RetryAfterMs", out var value) &&
            value.IsNumeric)
        {
            return TimeSpan.FromMilliseconds(value.ToDouble());
        }

        return null; // 429 with no explicit hint → the caller's exponential backoff applies.
    }
}
