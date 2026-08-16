using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Extensions.DiagnosticSources;
using Microsoft.Extensions.Options;

namespace AK.Products.Infrastructure.Persistence;

public sealed class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public MongoDbContext(IOptions<MongoDbSettings> settings)
    {
        var s = settings.Value;

        // The product catalogue runs on Azure Cosmos DB via its MongoDB API, which is
        // wire-compatible — the same MongoDB driver and data-access code are used unchanged.
        // The connection string is a SECRET resolved from Key Vault at runtime (see the
        // Infrastructure ServiceCollectionExtensions); it never appears in committed config.
        // Subscribe the diagnostics event subscriber on the cluster so the driver emits command
        // activities (ActivitySource "MongoDB.Driver.Core.Extensions.DiagnosticSources"), which OTel
        // records — otherwise Cosmos (Mongo API) reads/writes are invisible in every trace.
        var mongoSettings = MongoClientSettings.FromConnectionString(s.ConnectionString);
        mongoSettings.ClusterConfigurator = cb => cb.Subscribe(new DiagnosticsActivityEventSubscriber());
        var client = new MongoClient(mongoSettings);
        _database = client.GetDatabase(s.DatabaseName);

        EnsureShardedCollection(s.DatabaseName, s.ProductsCollection);
        CreateIndexes(s.ProductsCollection);
    }

    public MongoDbContext(IMongoDatabase database)
    {
        _database = database;
    }

    public IMongoCollection<T> GetCollection<T>(string collectionName) =>
        _database.GetCollection<T>(collectionName);

    // A real round-trip used by the DEEP Cosmos health check ({ ping: 1 } is the cheapest command
    // the server answers). Deliberately a single low-cost call — and exposed only on the diagnostic
    // endpoint, never on liveness/readiness.
    public Task PingAsync(CancellationToken ct = default) =>
        _database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);

    // Cosmos DB physically distributes a collection's documents across partitions by a SHARD
    // (partition) KEY. The Cosmos DB for MongoDB RU/serverless API requires a SINGLE-FIELD,
    // HASHED shard key. We shard on the product id (`_id`):
    //   - high cardinality (one value per product) → documents spread evenly, no hot partition;
    //   - point-reads by id are single-partition and cheap (~1 RU);
    //   - category/sub-category browse is a low-cost cross-partition query at this catalogue
    //     scale, served by the secondary indexes below.
    // The shard key is fixed when the collection is created; this command is a no-op if the
    // collection is already sharded with the same key. On a standalone MongoDB (the local-dev
    // fallback), sharding is unsupported and the command is skipped — the app still runs.
    private void EnsureShardedCollection(string databaseName, string collectionName)
    {
        var command = new BsonDocument
        {
            { "shardCollection", $"{databaseName}.{collectionName}" },
            { "key", new BsonDocument { { "_id", "hashed" } } }
        };

        try
        {
            _database.RunCommand<BsonDocument>(command);
        }
        catch (MongoCommandException)
        {
            // Not supported (standalone MongoDB used for local development) or the collection is
            // already sharded with this key — either way, safe to continue.
        }
    }

    private void CreateIndexes(string collectionName)
    {
        var products = _database.GetCollection<Domain.Entities.Product>(collectionName);

        // SKU lookup index. NOTE: on a SHARDED Cosmos collection a unique index must include the
        // shard key (`_id`), so a stand-alone unique constraint on SKU is not enforceable here —
        // SKU uniqueness is owned by the seed/data layer. This (non-unique) index keeps SKU
        // lookups efficient.
        products.Indexes.CreateOne(new CreateIndexModel<Domain.Entities.Product>(
            Builders<Domain.Entities.Product>.IndexKeys.Ascending(p => p.SKU),
            new CreateIndexOptions { Name = "idx_sku" }));

        // Secondary indexes for category / sub-category browse and status filtering — supported
        // cross-partition query patterns at this scale.
        products.Indexes.CreateOne(new CreateIndexModel<Domain.Entities.Product>(
            Builders<Domain.Entities.Product>.IndexKeys.Ascending(p => p.CategoryName),
            new CreateIndexOptions { Name = "idx_category" }));

        products.Indexes.CreateOne(new CreateIndexModel<Domain.Entities.Product>(
            Builders<Domain.Entities.Product>.IndexKeys.Ascending(p => p.SubCategoryName),
            new CreateIndexOptions { Name = "idx_subcategory" }));

        products.Indexes.CreateOne(new CreateIndexModel<Domain.Entities.Product>(
            Builders<Domain.Entities.Product>.IndexKeys.Ascending(p => p.Status),
            new CreateIndexOptions { Name = "idx_status" }));

        // The previous text index on Name/Brand/Description is removed: Cosmos DB for MongoDB API
        // does not support text ($text) indexes. The field indexes above cover the browse paths;
        // full-text search would be served by a dedicated search service if introduced later.
    }
}
