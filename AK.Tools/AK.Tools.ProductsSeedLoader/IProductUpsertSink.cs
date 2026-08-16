using ProductEntity = AK.Products.Domain.Entities.Product;

namespace AK.Tools.ProductsSeedLoader;

// Where the loader writes each product. Abstracted so the load logic (CSV -> Product -> upsert)
// can be unit-tested with a mock, no live Cosmos required.
public interface IProductUpsertSink
{
    Task UpsertAsync(ProductEntity product, CancellationToken ct = default);
}
