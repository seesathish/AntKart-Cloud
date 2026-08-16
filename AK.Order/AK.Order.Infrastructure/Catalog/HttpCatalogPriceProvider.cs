using System.Net;
using System.Net.Http.Json;
using AK.Order.Application.Common.Exceptions;
using AK.Order.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace AK.Order.Infrastructure.Catalog;

// Reads authoritative product prices from AK.Products' GET /api/v1/products/{id} (AllowAnonymous —
// no token needed). The resilience pipeline (retry → circuit breaker → timeout) is attached at DI
// registration; this client only translates results and FAILS CLOSED on any unreachable outcome.
internal sealed class HttpCatalogPriceProvider(HttpClient http, ILogger<HttpCatalogPriceProvider> logger)
    : ICatalogPriceProvider
{
    // Local deserialisation shape — only the fields we need. Deliberately NOT a reference to
    // AK.Products.* (no cross-service project coupling; denormalise the contract here).
    private sealed record ProductPriceDto(string Id, string Status, decimal Price, decimal? DiscountPrice);

    public async Task<IReadOnlyDictionary<string, CatalogPriceResult>> GetEffectivePricesAsync(
        IReadOnlyCollection<string> productIds, CancellationToken ct)
    {
        var results = new Dictionary<string, CatalogPriceResult>();

        foreach (var id in productIds.Distinct())
        {
            try
            {
                using var response = await http.GetAsync($"api/v1/products/{id}", ct);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    results[id] = new CatalogPriceResult(CatalogPriceStatus.NotFound, 0m);
                    continue;
                }

                // Any non-success status that survived the retries is treated as unavailable — we
                // must never price an order from an unverified response.
                response.EnsureSuccessStatusCode();

                var dto = await response.Content.ReadFromJsonAsync<ProductPriceDto>(ct)
                    ?? throw new CatalogUnavailableException($"Empty product response for '{id}'.");

                var status = string.Equals(dto.Status, "Active", StringComparison.OrdinalIgnoreCase)
                    ? CatalogPriceStatus.Found
                    : CatalogPriceStatus.Inactive;

                // Effective price is discount-aware: DiscountPrice when present, else the base Price.
                results[id] = new CatalogPriceResult(status, dto.DiscountPrice ?? dto.Price);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or BrokenCircuitException)
            {
                // Transport failure, timeout (incl. the resilience pipeline's), or an open circuit:
                // the catalogue is unreachable. Log once and fail closed.
                logger.LogWarning(ex, "Catalogue price lookup failed for {ProductId}; failing closed.", id);
                throw new CatalogUnavailableException(
                    "Product catalogue is unavailable for price verification.", ex);
            }
        }

        return results;
    }
}
