namespace AK.Order.Application.Common.Exceptions;

// Raised when the product catalogue cannot be reached for price verification after the resilience
// pipeline (retry / circuit breaker / timeout) is exhausted. The order-creation flow catches this
// and FAILS CLOSED — it never prices or persists an order from an unverified source.
public sealed class CatalogUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
