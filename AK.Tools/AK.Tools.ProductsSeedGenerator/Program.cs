using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

// =============================================================================
// Product seed-data generator (AK.Seed-Data)
// =============================================================================
// Produces a DETERMINISTIC catalogue of ~3,000 products as AK.Seed-Data/products.csv.
//
// Deterministic = a fixed RNG seed + a fixed iteration order, so regenerating the file always
// yields byte-identical output. The CSV is committed DATA (not a secret); the loader upserts it
// into Cosmos. Run with:  dotnet run --project AK.Tools.ProductsSeedGenerator
//
// The columns mirror the AK.Products domain model exactly (Name, Description, SKU, Brand,
// CategoryName, SubCategoryName, Price, Currency, StockQuantity, Sizes, Colors, Material). The
// document id is NOT stored here — the loader derives it deterministically from the SKU.

const int Seed = 42;
const int PerSubCategory = 100; // 3 categories x 10 subcategories x 100 = 3,000 products

var outputPath = args.Length > 0 ? args[0] : Path.Combine(FindRepoRoot(), "AK.Seed-Data", "products.csv");
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

var rows = Generate(Seed, PerSubCategory);

// LF newline + invariant culture → identical bytes on every platform/run.
var config = new CsvConfiguration(CultureInfo.InvariantCulture) { NewLine = "\n" };
using (var writer = new StreamWriter(outputPath, append: false))
using (var csv = new CsvWriter(writer, config))
{
    csv.WriteRecords(rows);
}

Console.WriteLine($"Generated {rows.Count} products -> {outputPath}");
return;

static List<GenRow> Generate(int seed, int perSubCategory)
{
    var rng = new Random(seed);

    string[] adjectives =
    [
        "Classic", "Premium", "Urban", "Sporty", "Slim", "Relaxed",
        "Modern", "Vintage", "Essential", "Signature", "Everyday", "Designer"
    ];

    var rows = new List<GenRow>();

    foreach (var cat in Categories())
    {
        foreach (var sub in cat.SubCategories)
        {
            var abbrev = Abbrev(sub.Display);
            var sizes = sub.Kind switch
            {
                Kind.Footwear => cat.FootwearSizes,
                Kind.Accessory => ["One Size"],
                _ => cat.ApparelSizes
            };
            var materials = sub.Kind == Kind.Accessory ? cat.AccessoryMaterials : cat.Materials;

            for (var i = 1; i <= perSubCategory; i++)
            {
                var brand = cat.Brands[rng.Next(cat.Brands.Length)];
                var adjective = adjectives[rng.Next(adjectives.Length)];
                var material = materials[rng.Next(materials.Length)];
                var price = rng.Next(cat.PriceMin, cat.PriceMax) + 0.99m;
                var stock = rng.Next(0, 201);
                var colors = cat.Colors.OrderBy(_ => rng.Next()).Take(rng.Next(1, 4)).ToList();

                rows.Add(new GenRow
                {
                    Sku = $"{cat.SkuPrefix}-{abbrev}-{i:D3}",
                    Name = $"{brand} {adjective} {sub.Display}",
                    Description = string.Format(
                        CultureInfo.InvariantCulture, cat.DescriptionTemplate,
                        sub.Display.ToLowerInvariant(), cat.Name.ToLowerInvariant(), material.ToLowerInvariant()),
                    Brand = brand,
                    CategoryName = cat.Name,
                    SubCategoryName = sub.Display,
                    Price = price,
                    Currency = "USD",
                    StockQuantity = stock,
                    Sizes = string.Join("|", sizes),
                    Colors = string.Join("|", colors),
                    Material = material
                });
            }
        }
    }

    return rows;
}

// First four letters (letters only, uppercased) of the subcategory — the SKU's middle segment.
// Subcategory names within a category are chosen so these are unique, keeping every SKU unique.
static string Abbrev(string s)
{
    var letters = new string(s.Where(char.IsLetter).ToArray()).ToUpperInvariant();
    return letters[..Math.Min(4, letters.Length)];
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AntKart.sln")))
        dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

static IEnumerable<Cat> Categories() =>
[
    new Cat(
        Name: "Men", SkuPrefix: "MEN",
        Brands: ["Arrow", "Peter England", "Louis Philippe", "Raymond", "HRX", "Wrangler", "Levis", "Zara Man", "Jack & Jones", "US Polo"],
        Colors: ["Navy Blue", "White", "Black", "Grey", "Khaki", "Olive", "Brown", "Maroon", "Charcoal", "Sky Blue"],
        Materials: ["Cotton", "Denim", "Linen", "Wool", "Polyester", "Leather", "Canvas"],
        AccessoryMaterials: ["Leather", "Stainless Steel", "Canvas", "Nylon"],
        ApparelSizes: ["S", "M", "L", "XL", "XXL"],
        FootwearSizes: ["UK6", "UK7", "UK8", "UK9", "UK10", "UK11"],
        PriceMin: 499, PriceMax: 4999,
        DescriptionTemplate: "Premium quality {0} for {1}. Crafted from {2} for lasting comfort and style.",
        SubCategories:
        [
            new SubCat("Shirts", Kind.Apparel), new SubCat("T-Shirts", Kind.Apparel),
            new SubCat("Jeans", Kind.Apparel), new SubCat("Trousers", Kind.Apparel),
            new SubCat("Jackets", Kind.Apparel), new SubCat("Sneakers", Kind.Footwear),
            new SubCat("Formal Shoes", Kind.Footwear), new SubCat("Belts", Kind.Accessory),
            new SubCat("Watches", Kind.Accessory), new SubCat("Backpacks", Kind.Accessory)
        ]),
    new Cat(
        Name: "Women", SkuPrefix: "WOM",
        Brands: ["W for Woman", "Biba", "FabIndia", "Mango", "Zara Women", "AND", "Global Desi", "Aurelia", "Vero Moda", "Only"],
        Colors: ["Rose Pink", "Mint Green", "Coral", "Ivory", "Teal", "Mauve", "Burgundy", "Royal Blue", "Peach", "Mustard"],
        Materials: ["Cotton", "Silk", "Georgette", "Chiffon", "Linen", "Crepe", "Suede"],
        AccessoryMaterials: ["Leather", "Gold-Plated", "Silk", "Suede"],
        ApparelSizes: ["XS", "S", "M", "L", "XL"],
        FootwearSizes: ["UK3", "UK4", "UK5", "UK6", "UK7", "UK8"],
        PriceMin: 599, PriceMax: 5599,
        DescriptionTemplate: "Elegant {0} for {1}. Made with fine {2} for an effortless look.",
        SubCategories:
        [
            new SubCat("Dresses", Kind.Apparel), new SubCat("Tops", Kind.Apparel),
            new SubCat("Skirts", Kind.Apparel), new SubCat("Kurtis", Kind.Apparel),
            new SubCat("Sarees", Kind.Apparel), new SubCat("Heels", Kind.Footwear),
            new SubCat("Flats", Kind.Footwear), new SubCat("Handbags", Kind.Accessory),
            new SubCat("Jewellery", Kind.Accessory), new SubCat("Scarves", Kind.Accessory)
        ]),
    new Cat(
        Name: "Kids", SkuPrefix: "KID",
        Brands: ["H&M Kids", "Zara Kids", "Gap Kids", "FirstCry", "Mothercare", "Gini & Jony", "US Polo Kids", "Lilliput", "Toffyhouse", "Babyhug"],
        Colors: ["Bright Red", "Sky Blue", "Sunny Yellow", "Lime Green", "Hot Pink", "Orange", "Purple", "Aqua", "Lavender", "Pastel Blue"],
        Materials: ["Cotton", "Fleece", "Denim", "Knit", "Organic Cotton", "Canvas", "Mesh"],
        AccessoryMaterials: ["Cotton", "Canvas", "Nylon", "Polyester"],
        ApparelSizes: ["2-3Y", "3-4Y", "4-5Y", "5-6Y", "6-7Y", "7-8Y", "9-10Y", "11-12Y"],
        FootwearSizes: ["C8", "C9", "C10", "C11", "C12", "C13"],
        PriceMin: 199, PriceMax: 1699,
        DescriptionTemplate: "Comfortable and durable {0} for {1}. Made with soft {2}, easy to wear and care for.",
        SubCategories:
        [
            new SubCat("T-Shirts", Kind.Apparel), new SubCat("Frocks", Kind.Apparel),
            new SubCat("Shorts", Kind.Apparel), new SubCat("Hoodies", Kind.Apparel),
            new SubCat("Nightwear", Kind.Apparel), new SubCat("Sneakers", Kind.Footwear),
            new SubCat("Sandals", Kind.Footwear), new SubCat("Caps", Kind.Accessory),
            new SubCat("School Bags", Kind.Accessory), new SubCat("Socks", Kind.Accessory)
        ])
];

internal enum Kind { Apparel, Footwear, Accessory }

internal sealed record SubCat(string Display, Kind Kind);

internal sealed record Cat(
    string Name, string SkuPrefix, string[] Brands, string[] Colors, string[] Materials,
    string[] AccessoryMaterials, string[] ApparelSizes, string[] FootwearSizes,
    int PriceMin, int PriceMax, string DescriptionTemplate, SubCat[] SubCategories);

// One CSV row. Property names are the column headers; the loader reads them by name.
internal sealed record GenRow
{
    public string Sku { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Brand { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public string SubCategoryName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Currency { get; init; } = "USD";
    public int StockQuantity { get; init; }
    public string Sizes { get; init; } = string.Empty;
    public string Colors { get; init; } = string.Empty;
    public string Material { get; init; } = string.Empty;
}
