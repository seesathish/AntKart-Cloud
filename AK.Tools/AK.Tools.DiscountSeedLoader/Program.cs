using AK.BuildingBlocks.Configuration;
using AK.Discount.Infrastructure.Extensions;
using AK.Products.Infrastructure.Persistence;
using AK.Tools.DiscountSeedLoader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// =============================================================================
// Discount seed loader
// =============================================================================
// Seeds discount coupons into AKDiscountDb (PostgreSQL), correlated to the REAL products already in
// Cosmos: every coupon's ProductId is a product's Cosmos `_id`, so AK.Products' gRPC
// GetDiscount(product_id) finds a coupon and computes discountPrice.
//
// SECRET-LESS, reusing the existing service foundations:
//   * READ products from Cosmos via the Products MongoDbContext — non-secret MongoDbSettings from
//     appsettings; the Cosmos connection string is resolved from Key Vault
//     ("ProductsCosmosConnectionString") via DefaultAzureCredential.
//   * WRITE coupons via the Discount service's DiscountContext (AddDiscountInfrastructure) — the
//     AKDiscountDb connection string is resolved from Key Vault ("ConnectionStrings--DiscountDb").
//
// Idempotent (upsert by ProductId — one coupon per product). Safe to run repeatedly.
// Run:  dotnet run --project AK.Tools.DiscountSeedLoader   (needs an az-login / managed identity with
//       Key Vault Secrets User + Cosmos + Postgres access).

// 1. Configuration — appsettings + env vars, then fold in Key Vault secrets.
var configBuilder = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables();
var preliminary = configBuilder.Build();
configBuilder.AddAzureKeyVaultConfiguration(preliminary);
var configuration = configBuilder.Build();

// 2. DI.
var services = new ServiceCollection();
services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true));

// Read side — reuse the Products persistence layer (MongoDbContext + ProductClassMap), no duplication.
services.Configure<MongoDbSettings>(configuration.GetSection("MongoDbSettings"));
services.PostConfigure<MongoDbSettings>(s =>
{
    var cosmos = configuration["ProductsCosmosConnectionString"];
    if (!string.IsNullOrWhiteSpace(cosmos))
        s.ConnectionString = cosmos;
});
ProductClassMap.Register();
services.AddSingleton<MongoDbContext>();
services.AddSingleton<IProductSource, MongoProductSource>();

// Write side — reuse the Discount service's registration (DiscountContext + Npgsql, connection from
// "DiscountDb"). The seeder-specific upsert sink and orchestrator are scoped alongside the context.
services.AddDiscountInfrastructure(configuration);
services.AddScoped<ICouponUpsertSink, EfCouponUpsertSink>();
services.AddScoped<DiscountSeedLoader>();

await using var provider = services.BuildServiceProvider();

// 3. Run inside a scope (DiscountContext is scoped).
using var scope = provider.CreateScope();
var loader = scope.ServiceProvider.GetRequiredService<DiscountSeedLoader>();

Console.WriteLine("Seeding discount coupons correlated to Cosmos products ...");
var summary = await loader.RunAsync();
Console.WriteLine(
    $"Done. Cleared {summary.CouponsCleared} existing coupons, then seeded {summary.TotalCoupons} " +
    $"into AKDiscountDb from {summary.ProductsRead} products (re-running converges to the same set).");
return 0;
