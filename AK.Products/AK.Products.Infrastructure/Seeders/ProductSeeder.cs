using AK.Products.Domain.Entities;
using AK.Products.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace AK.Products.Infrastructure.Seeders;

public sealed class ProductSeeder
{
    private readonly IMongoCollection<Product> _collection;
    private static readonly Random _rng = new(42);

    public ProductSeeder(MongoDbContext context, IOptions<MongoDbSettings> settings)
    {
        _collection = context.GetCollection<Product>(settings.Value.ProductsCollection);
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var count = await _collection.CountDocumentsAsync(_ => true, null, ct);
        if (count >= 300) return;

        await _collection.DeleteManyAsync(_ => true, ct);
        var products = GenerateProducts();
        await _collection.InsertManyAsync(products, null, ct);
    }

    private sealed record CategoryDefinition(
        string CategoryName,
        string SkuPrefix,
        string[] SubCategories,
        Dictionary<string, string[]> ProductNames,
        string[] Brands,
        string[] Sizes,
        string[] Colors,
        string[] Materials,
        decimal PriceMin,
        decimal PriceMax,
        string DescriptionTemplate
    );

    private static List<Product> GenerateProducts()
    {
        var categories = new[]
        {
            new CategoryDefinition(
                CategoryName: "Men",
                SkuPrefix: "MEN",
                SubCategories: ["Shirts", "Pants", "Jackets", "Suits", "Casual Wear", "T-Shirts", "Jeans", "Shorts", "Blazers", "Ethnic Wear"],
                ProductNames: new Dictionary<string, string[]>
                {
                    ["Shirts"]      = ["Classic Formal Shirt", "Casual Oxford Shirt", "Slim Fit Dress Shirt", "Mandarin Collar Shirt", "Check Print Shirt"],
                    ["Pants"]       = ["Slim Fit Chinos", "Regular Fit Trousers", "Cargo Pants", "Linen Trousers", "Formal Dress Pants"],
                    ["Jackets"]     = ["Leather Biker Jacket", "Denim Jacket", "Bomber Jacket", "Sports Jacket", "Windbreaker"],
                    ["Suits"]       = ["Classic Business Suit", "Wedding Tuxedo", "Single Breasted Suit", "Slim Fit Suit", "Double Breasted Suit"],
                    ["Casual Wear"] = ["Weekend Casual Set", "Relaxed Fit Polo", "Graphic Print Tee", "Henley Top", "Button Down Casual"],
                    ["T-Shirts"]    = ["Plain Round Neck Tee", "V-Neck T-Shirt", "Polo T-Shirt", "Striped T-Shirt", "Printed T-Shirt"],
                    ["Jeans"]       = ["Slim Fit Jeans", "Regular Fit Jeans", "Skinny Jeans", "Straight Cut Jeans", "Ripped Jeans"],
                    ["Shorts"]      = ["Cargo Shorts", "Chino Shorts", "Beach Shorts", "Sports Shorts", "Casual Bermuda"],
                    ["Blazers"]     = ["Linen Blazer", "Formal Blazer", "Casual Sports Blazer", "Check Blazer", "Velvet Blazer"],
                    ["Ethnic Wear"] = ["Kurta Pajama Set", "Dhoti Kurta", "Sherwani", "Nehru Jacket", "Pathani Suit"]
                },
                Brands: ["ArrowMen", "PeterEngland", "Louis Philippe", "Raymond", "HRX", "UCB", "Wrangler", "Levis", "Zara Man", "H&M Men"],
                Sizes: ["S", "M", "L", "XL", "XXL"],
                Colors: ["Navy Blue", "White", "Black", "Grey", "Khaki", "Olive", "Brown", "Maroon", "Charcoal", "Sky Blue"],
                Materials: ["Cotton", "Polyester", "Linen", "Wool", "Denim", "Silk Blend", "Rayon"],
                PriceMin: 499m,
                PriceMax: 4999m,
                DescriptionTemplate: "Premium quality {0} for {1}. Made with {2} fabric. Perfect for all occasions."
            ),
            new CategoryDefinition(
                CategoryName: "Women",
                SkuPrefix: "WOM",
                SubCategories: ["Dresses", "Tops", "Skirts", "Blouses", "Jackets", "Kurtis", "Sarees", "Lehenga", "Jumpsuits", "Ethnic Fusion"],
                ProductNames: new Dictionary<string, string[]>
                {
                    ["Dresses"]       = ["Floral Maxi Dress", "Bodycon Dress", "A-Line Dress", "Wrap Dress", "Off-Shoulder Dress"],
                    ["Tops"]          = ["Crop Top", "Peplum Top", "Fitted Tank Top", "Flowy Blouse Top", "Cold Shoulder Top"],
                    ["Skirts"]        = ["Pleated Midi Skirt", "Mini Skirt", "Flared Skirt", "Pencil Skirt", "Wrap Skirt"],
                    ["Blouses"]       = ["Silk Blouse", "Embroidered Blouse", "Ruffled Blouse", "Formal Blouse", "Casual Blouse"],
                    ["Jackets"]       = ["Cropped Jacket", "Blazer Jacket", "Denim Jacket", "Puffer Vest", "Trench Coat"],
                    ["Kurtis"]        = ["Straight Kurti", "Anarkali Kurti", "A-Line Kurti", "Flared Kurti", "Short Kurti"],
                    ["Sarees"]        = ["Silk Saree", "Cotton Saree", "Georgette Saree", "Chiffon Saree", "Linen Saree"],
                    ["Lehenga"]       = ["Bridal Lehenga", "Party Lehenga", "Casual Lehenga", "Embroidered Lehenga", "Floral Lehenga"],
                    ["Jumpsuits"]     = ["Casual Jumpsuit", "Formal Jumpsuit", "Printed Jumpsuit", "Belted Jumpsuit", "Palazzo Jumpsuit"],
                    ["Ethnic Fusion"] = ["Indo-Western Set", "Dhoti Pants Set", "Fusion Salwar Suit", "Modern Ethnic Top", "Palazzo Set"]
                },
                Brands: ["W for Woman", "Biba", "Fabindia", "Mango", "Zara Women", "H&M Women", "AND", "Global Desi", "Aurelia", "Vero Moda"],
                Sizes: ["XS", "S", "M", "L", "XL", "XXL"],
                Colors: ["Rose Pink", "Mint Green", "Coral", "Ivory", "Teal", "Mauve", "Burgundy", "Royal Blue", "Peach", "Mustard Yellow"],
                Materials: ["Chiffon", "Georgette", "Crepe", "Silk", "Cotton", "Linen", "Rayon", "Jersey"],
                PriceMin: 599m,
                PriceMax: 5599m,
                DescriptionTemplate: "Elegant and stylish {0} for {1}. Crafted with premium {2}. Perfect for every occasion."
            ),
            new CategoryDefinition(
                CategoryName: "Kids",
                SkuPrefix: "KID",
                SubCategories: ["T-Shirts", "Pants", "Dresses", "Jumpsuits", "School Wear", "Party Wear", "Ethnic Wear", "Nightwear", "Jackets", "Shorts"],
                ProductNames: new Dictionary<string, string[]>
                {
                    ["T-Shirts"]   = ["Cartoon Print Tee", "Striped T-Shirt", "Superhero Tee", "Plain Round Neck", "Graphic Tee"],
                    ["Pants"]      = ["Elastic Waist Pants", "Jogger Pants", "Cargo Pants", "Slim Fit Jeans", "Corduroys"],
                    ["Dresses"]    = ["Frock with Bow", "Floral Sundress", "Party Frock", "Casual Dress", "Princess Dress"],
                    ["Jumpsuits"]  = ["Dungaree Jumpsuit", "Playsuit", "Romper", "Bibshort", "Overall Set"],
                    ["School Wear"]= ["School Uniform Shirt", "School Trousers", "School Dress", "School Blazer", "PT Uniform"],
                    ["Party Wear"] = ["Birthday Party Dress", "Ethnic Party Set", "Formal Party Suit", "Princess Gown", "Tuxedo Set"],
                    ["Ethnic Wear"]= ["Kids Kurta Set", "Sherwani for Boys", "Lehenga Choli", "Dhoti Kurta", "Anarkali Suit"],
                    ["Nightwear"]  = ["Cartoon Pajama Set", "Night Suit", "Sleep Romper", "Onesie", "Star Print Nightwear"],
                    ["Jackets"]    = ["Puffer Jacket", "Hooded Sweatshirt", "Windcheater", "Denim Jacket", "Fleece Hoodie"],
                    ["Shorts"]     = ["Casual Shorts", "Cargo Shorts", "Swimming Shorts", "Cycle Shorts", "Sports Shorts"]
                },
                Brands: ["H&M Kids", "Zara Kids", "Gap Kids", "FirstCry", "Mothercare", "Gini & Jony", "Allen Solly Junior", "US Polo Kids", "Lilliput", "Toffyhouse"],
                Sizes: ["3-4Y", "4-5Y", "5-6Y", "6-7Y", "7-8Y", "8-9Y", "9-10Y", "10-11Y", "11-12Y"],
                Colors: ["Bright Red", "Sky Blue", "Sunny Yellow", "Lime Green", "Hot Pink", "Orange", "Purple", "Aqua", "Lavender", "Pastel Blue"],
                Materials: ["Soft Cotton", "Fleece", "Denim", "Jersey", "Knit", "Velvet", "Organic Cotton"],
                PriceMin: 199m,
                PriceMax: 1699m,
                DescriptionTemplate: "Adorable and comfortable {0} for {1}. Made with soft {2}. Easy to wear and care for."
            )
        };

        var products = new List<Product>();

        foreach (var cat in categories)
        {
            int idx = 0;
            foreach (var sub in cat.SubCategories)
            {
                var names = cat.ProductNames.TryGetValue(sub, out var n) ? n
                    : [$"{sub} Style A", $"{sub} Style B", $"{sub} Style C", $"{sub} Style D", $"{sub} Style E"];

                var subAbbrev = sub.Replace(" ", "").ToUpper();
                subAbbrev = subAbbrev[..Math.Min(4, subAbbrev.Length)];

                for (int i = 0; i < 10; i++)
                {
                    var brand = cat.Brands[_rng.Next(cat.Brands.Length)];
                    var productName = names[i % names.Length];
                    var suffix = i / names.Length > 0 ? $" v{i / names.Length + 1}" : "";
                    var fullName = $"{brand} {productName}{suffix}";
                    var price = Math.Round((decimal)(_rng.NextDouble() * (double)(cat.PriceMax - cat.PriceMin)) + cat.PriceMin, 2);
                    var stock = _rng.Next(0, 201);
                    var colors = cat.Colors.OrderBy(_ => _rng.Next()).Take(_rng.Next(1, 4)).ToList();
                    var sizes = cat.Sizes.OrderBy(_ => _rng.Next()).Take(_rng.Next(2, 5)).ToList();
                    var material = cat.Materials[_rng.Next(cat.Materials.Length)];
                    var sku = $"{cat.SkuPrefix}-{subAbbrev}-{(idx + 1):D3}";
                    var description = string.Format(cat.DescriptionTemplate, fullName.ToLower(), cat.CategoryName.ToLower(), material);

                    var product = Product.Create(fullName, description, sku, brand,
                        cat.CategoryName, sub, price, "USD", stock, sizes, colors, material);

                    if (_rng.Next(5) == 0) product.SetFeatured(true);
                    if (stock > 0 && _rng.NextDouble() > 0.65)
                        product.SetDiscount(Math.Round(price * (decimal)(0.55 + _rng.NextDouble() * 0.35), 2));

                    products.Add(product);
                    idx++;
                }
            }
        }

        return products;
    }
}
