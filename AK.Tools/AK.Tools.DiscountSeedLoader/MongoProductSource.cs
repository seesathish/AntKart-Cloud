using AK.Products.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using ProductEntity = AK.Products.Domain.Entities.Product;

namespace AK.Tools.DiscountSeedLoader;

// Reads every product from the Cosmos `products` collection, reusing the Products service's
// MongoDbContext (no duplicated Mongo client/mapping code). Projects each product to a SeedProduct,
// carrying the Cosmos `_id` so the generated coupons reference REAL product ids.
public sealed class MongoProductSource : IProductSource
{
    private readonly IMongoCollection<ProductEntity> _collection;

    public MongoProductSource(MongoDbContext context, IOptions<MongoDbSettings> settings)
        => _collection = context.GetCollection<ProductEntity>(settings.Value.ProductsCollection);

    public async Task<IReadOnlyList<SeedProduct>> GetProductsAsync(CancellationToken ct = default)
    {
        var products = await _collection.Find(FilterDefinition<ProductEntity>.Empty).ToListAsync(ct);
        return products
            .Select(p => new SeedProduct(p.Id, p.Name, p.SKU, p.CategoryName, p.SubCategoryName, p.Price, p.Currency))
            .ToList();
    }
}
