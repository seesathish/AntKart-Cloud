using AK.Products.Application.Interfaces;
using AK.Products.Domain.Entities;
using AK.Products.Domain.Specifications;
using MongoDB.Driver;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace AK.Products.Infrastructure.Persistence.Repositories;

public sealed class ProductRepository : IProductRepository
{
    private readonly IMongoCollection<Product> _collection;

    // The "cosmos" pipeline retries transient Cosmos faults (429 / timeout / dropped connection)
    // and HONOURS the server's Retry-After on a 429. Every data-store call below runs through it,
    // so the resilience lives exactly where the call is made — not hidden in the driver.
    private readonly ResiliencePipeline _cosmos;

    public ProductRepository(
        MongoDbContext context,
        IOptions<MongoDbSettings> settings,
        ResiliencePipelineProvider<string> pipelines)
    {
        _collection = context.GetCollection<Product>(settings.Value.ProductsCollection);
        _cosmos = pipelines.GetPipeline("cosmos");
    }

    public async Task<Product?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _cosmos.ExecuteAsync(
            async token => await _collection.Find(p => p.Id == id).FirstOrDefaultAsync(token), ct);

    public async Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default) =>
        await _cosmos.ExecuteAsync(
            async token => await _collection.Find(p => p.SKU == sku).FirstOrDefaultAsync(token), ct);

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct = default)
    {
        var results = await _cosmos.ExecuteAsync(
            async token => await _collection.Find(_ => true).ToListAsync(token), ct);
        return results.AsReadOnly();
    }

    public async Task<IReadOnlyList<Product>> GetByCategoryAsync(string category, CancellationToken ct = default)
    {
        var results = await _cosmos.ExecuteAsync(
            async token => await _collection.Find(p => p.CategoryName == category).ToListAsync(token), ct);
        return results.AsReadOnly();
    }

    public async Task<IReadOnlyList<string>> GetDistinctCategoriesAsync(CancellationToken ct = default)
    {
        var filter = Builders<Product>.Filter.Empty;
        var list = await _cosmos.ExecuteAsync(async token =>
        {
            var results = await _collection.DistinctAsync<string>("CategoryName", filter, null, token);
            return await results.ToListAsync(token);
        }, ct);
        return list.OrderBy(c => c).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<Product>> ListAsync(ISpecification<Product> spec, CancellationToken ct = default)
    {
        var filter = Builders<Product>.Filter.Where(spec.Criteria);
        var results = await _cosmos.ExecuteAsync(async token =>
        {
            var query = _collection.Find(filter);

            if (spec.OrderBy is not null)
                query = query.SortBy(spec.OrderBy);
            else if (spec.OrderByDescending is not null)
                query = query.SortByDescending(spec.OrderByDescending);

            if (spec.IsPagingEnabled)
                query = query.Skip(spec.Skip).Limit(spec.Take);

            return await query.ToListAsync(token);
        }, ct);
        return results.AsReadOnly();
    }

    public async Task<int> CountAsync(ISpecification<Product> spec, CancellationToken ct = default)
    {
        var filter = Builders<Product>.Filter.Where(spec.Criteria);
        return (int)await _cosmos.ExecuteAsync(
            async token => await _collection.CountDocumentsAsync(filter, null, token), ct);
    }

    public async Task<Product> AddAsync(Product product, CancellationToken ct = default)
    {
        await _cosmos.ExecuteAsync(
            async token => await _collection.InsertOneAsync(product, null, token), ct);
        return product;
    }

    public async Task UpdateAsync(Product product, CancellationToken ct = default) =>
        await _cosmos.ExecuteAsync(
            async token => await _collection.ReplaceOneAsync(
                p => p.Id == product.Id, product, new ReplaceOptions(), token), ct);

    public async Task DeleteAsync(string id, CancellationToken ct = default) =>
        await _cosmos.ExecuteAsync(
            async token => await _collection.DeleteOneAsync(p => p.Id == id, token), ct);

    public async Task BulkInsertAsync(IEnumerable<Product> products, CancellationToken ct = default) =>
        await _cosmos.ExecuteAsync(
            async token => await _collection.InsertManyAsync(products, null, token), ct);

    public async Task BulkUpdateAsync(IEnumerable<Product> products, CancellationToken ct = default)
    {
        var writeModels = products.Select(p =>
            new ReplaceOneModel<Product>(Builders<Product>.Filter.Where(x => x.Id == p.Id), p));
        await _cosmos.ExecuteAsync(
            async token => await _collection.BulkWriteAsync(writeModels, null, token), ct);
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken ct = default) =>
        await _cosmos.ExecuteAsync(
            async token => await _collection.CountDocumentsAsync(p => p.Id == id, null, token), ct) > 0;

    public async Task<bool> SkuExistsAsync(string sku, CancellationToken ct = default) =>
        await _cosmos.ExecuteAsync(
            async token => await _collection.CountDocumentsAsync(p => p.SKU == sku, null, token), ct) > 0;

    public async Task<IReadOnlyList<Product>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var results = await _cosmos.ExecuteAsync(async token => await _collection.Find(_ => true)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(token), ct);
        return results.AsReadOnly();
    }
}
