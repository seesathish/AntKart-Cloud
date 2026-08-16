namespace AK.Tools.ProductsSeedLoader;

// One row of AK.Seed-Data/products.csv. CsvHelper maps columns to these properties BY HEADER NAME,
// so the loader is independent of column order. List fields (Sizes, Colors) are pipe-delimited.
public sealed class ProductCsvRow
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? SubCategoryName { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public int StockQuantity { get; set; }
    public string? Sizes { get; set; }
    public string? Colors { get; set; }
    public string? Material { get; set; }
}
