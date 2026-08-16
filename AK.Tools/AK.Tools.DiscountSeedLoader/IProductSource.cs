namespace AK.Tools.DiscountSeedLoader;

// Where the loader reads products from (the source of truth for product ids). Abstracted so the
// load/selection logic can be unit-tested with a fake, no live Cosmos required.
public interface IProductSource
{
    Task<IReadOnlyList<SeedProduct>> GetProductsAsync(CancellationToken ct = default);
}
