using AK.Products.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using ProductEntity = AK.Products.Domain.Entities.Product;

namespace AK.Tools.ProductsSeedLoader;

// Upserts a product into the Cosmos `products` collection, reusing the Products service's
// MongoDbContext (no duplicated Mongo client/mapping code).
//
// The upsert filters on `_id` (the product's deterministic, SKU-derived id) with IsUpsert = true:
//   * existing document  -> replaced in place (a single-partition point write on the hashed _id);
//   * new document       -> inserted with that same id.
// Re-running the loader therefore converges to exactly one document per SKU — never duplicates.
public sealed class MongoProductUpsertSink : IProductUpsertSink
{
    private readonly IMongoCollection<ProductEntity> _collection;

    public MongoProductUpsertSink(MongoDbContext context, IOptions<MongoDbSettings> settings)
        => _collection = context.GetCollection<ProductEntity>(settings.Value.ProductsCollection);

    public Task UpsertAsync(ProductEntity product, CancellationToken ct = default) =>
        _collection.ReplaceOneAsync(
            Builders<ProductEntity>.Filter.Eq(p => p.Id, product.Id),
            product,
            new ReplaceOptions { IsUpsert = true },
            ct);
}
