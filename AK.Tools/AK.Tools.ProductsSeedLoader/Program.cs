using AK.BuildingBlocks.Configuration;
using AK.Products.Infrastructure.Persistence;
using AK.Tools.ProductsSeedLoader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// =============================================================================
// Product seed loader
// =============================================================================
// Reads AK.Seed-Data/products.csv and upserts every product into the Cosmos `products` collection.
// Idempotent (keyed on the SKU-derived document id) and SECRET-LESS — it reuses the Products
// service's configuration foundation:
//   * non-secret MongoDbSettings (database + collection names) from appsettings.json;
//   * the Cosmos connection string is a SECRET resolved from Key Vault via DefaultAzureCredential
//     (no secret in this tool or the repo), exactly like the Products service.
//
// Run:  dotnet run --project AK.Tools.ProductsSeedLoader   (needs an az-login / managed identity
//       with Key Vault Secrets User + Cosmos access). Safe to run repeatedly.

// 1. Configuration — appsettings + env vars, then fold in Key Vault secrets (incl. the Cosmos
//    connection string under "ProductsCosmosConnectionString").
var configBuilder = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables();
var preliminary = configBuilder.Build();
configBuilder.AddAzureKeyVaultConfiguration(preliminary);
var configuration = configBuilder.Build();

// 2. DI — reuse the Products persistence layer (MongoDbContext + ProductClassMap), no duplication.
var services = new ServiceCollection();
services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true));

services.Configure<MongoDbSettings>(configuration.GetSection("MongoDbSettings"));
services.PostConfigure<MongoDbSettings>(s =>
{
    var cosmos = configuration["ProductsCosmosConnectionString"];
    if (!string.IsNullOrWhiteSpace(cosmos))
        s.ConnectionString = cosmos;
});

ProductClassMap.Register();
services.AddSingleton<MongoDbContext>();
services.AddSingleton<IProductUpsertSink, MongoProductUpsertSink>();
services.AddSingleton<SeedLoader>();

await using var provider = services.BuildServiceProvider();

// 3. Load.
var csvPath = args.Length > 0 ? args[0] : Path.Combine(FindRepoRoot(), "AK.Seed-Data", "products.csv");
if (!File.Exists(csvPath))
{
    Console.Error.WriteLine($"Seed CSV not found: {csvPath}");
    return 1;
}

var loader = provider.GetRequiredService<SeedLoader>();
using var reader = new StreamReader(csvPath);

Console.WriteLine($"Loading products from {csvPath} ...");
var count = await loader.LoadAsync(reader);
Console.WriteLine($"Done. Upserted {count} products into the Cosmos 'products' collection (idempotent by SKU).");
return 0;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AntKart.sln")))
        dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
