namespace AK.Order.Application.Common.Interfaces;

// The authoritative price of a product, as the catalogue (AK.Products) reports it.
//   Found    — the product exists and is sellable; EffectivePrice is meaningful.
//   NotFound — no such product in the catalogue.
//   Inactive — the product exists but is not Active (EffectivePrice still carries the catalogue value).
public enum CatalogPriceStatus { Found, NotFound, Inactive }

// EffectivePrice is the discount-aware price the order must be charged at (DiscountPrice ?? Price).
// It is only authoritative when Status == Found; for NotFound it is 0, for Inactive it is informational.
public sealed record CatalogPriceResult(CatalogPriceStatus Status, decimal EffectivePrice);

// Reads the authoritative effective price for products from the catalogue. The order-creation flow
// uses this to price the order server-authoritatively and to decide whether to interrupt the
// customer. The catalogue is the source of truth — the client's submitted price is advisory.
public interface ICatalogPriceProvider
{
    // Resolves the effective price for each DISTINCT product id in one pass. Throws
    // CatalogUnavailableException if the catalogue cannot be reached after the resilience pipeline
    // is exhausted, so the caller can fail closed (never price an order from an unverified source).
    Task<IReadOnlyDictionary<string, CatalogPriceResult>> GetEffectivePricesAsync(
        IReadOnlyCollection<string> productIds, CancellationToken ct);
}
