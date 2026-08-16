using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using ProductEntity = AK.Products.Domain.Entities.Product;

namespace AK.Tools.ProductsSeedLoader;

// Reads AK.Seed-Data/products.csv and upserts each row. The CSV parsing and row->Product mapping are
// kept separate from the Mongo sink so both are unit-testable without live Cosmos.
public sealed class SeedLoader
{
    private readonly IProductUpsertSink _sink;
    private readonly ILogger<SeedLoader> _logger;

    public SeedLoader(IProductUpsertSink sink, ILogger<SeedLoader> logger)
    {
        _sink = sink;
        _logger = logger;
    }

    public async Task<int> LoadAsync(TextReader csv, CancellationToken ct = default)
    {
        using var reader = new CsvReader(csv, new CsvConfiguration(CultureInfo.InvariantCulture));

        var count = 0;
        await foreach (var row in reader.GetRecordsAsync<ProductCsvRow>(ct))
        {
            await _sink.UpsertAsync(ToProduct(row), ct);
            count++;
            if (count % 500 == 0)
                _logger.LogInformation("Upserted {Count} products...", count);
        }

        _logger.LogInformation("Upserted {Count} products (total).", count);
        return count;
    }

    // Maps a CSV row to a domain Product with a DETERMINISTIC, SKU-derived id (the basis of the
    // idempotent upsert). Pipe-delimited list fields are split back into lists.
    public static ProductEntity ToProduct(ProductCsvRow row)
    {
        var id = DeterministicId.FromSku(row.Sku);
        return ProductEntity.CreateForSeed(
            id,
            row.Name,
            row.Description,
            row.Sku,
            row.Brand,
            row.CategoryName,
            string.IsNullOrWhiteSpace(row.SubCategoryName) ? null : row.SubCategoryName,
            row.Price,
            string.IsNullOrWhiteSpace(row.Currency) ? "USD" : row.Currency,
            row.StockQuantity,
            SplitList(row.Sizes),
            SplitList(row.Colors),
            string.IsNullOrWhiteSpace(row.Material) ? null : row.Material);
    }

    private static List<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
