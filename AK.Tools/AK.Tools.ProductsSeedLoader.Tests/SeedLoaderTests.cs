using AK.Tools.ProductsSeedLoader;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ProductEntity = AK.Products.Domain.Entities.Product;

namespace AK.Tools.ProductsSeedLoader.Tests;

public sealed class SeedLoaderTests
{
    private const string Header =
        "Sku,Name,Description,Brand,CategoryName,SubCategoryName,Price,Currency,StockQuantity,Sizes,Colors,Material";

    [Fact]
    public void ToProduct_MapsAllFields_AndSplitsPipeLists()
    {
        var row = new ProductCsvRow
        {
            Sku = "MEN-SNEA-007",
            Name = "Levis Urban Sneakers",
            Description = "Premium quality sneakers for men.",
            Brand = "Levis",
            CategoryName = "Men",
            SubCategoryName = "Sneakers",
            Price = 2499.99m,
            Currency = "USD",
            StockQuantity = 42,
            Sizes = "UK7|UK8|UK9",
            Colors = "Black|White",
            Material = "Leather"
        };

        var product = SeedLoader.ToProduct(row);

        product.Id.Should().Be(DeterministicId.FromSku("MEN-SNEA-007"));
        product.SKU.Should().Be("MEN-SNEA-007");
        product.Name.Should().Be("Levis Urban Sneakers");
        product.CategoryName.Should().Be("Men");
        product.SubCategoryName.Should().Be("Sneakers");
        product.Price.Should().Be(2499.99m);
        product.Currency.Should().Be("USD");
        product.StockQuantity.Should().Be(42);
        product.Sizes.Should().Equal("UK7", "UK8", "UK9");
        product.Colors.Should().Equal("Black", "White");
        product.Material.Should().Be("Leather");
    }

    [Fact]
    public void ToProduct_IsIdempotentById_ForTheSameSku()
    {
        var row = new ProductCsvRow { Sku = "WOM-DRES-001", Name = "A", CategoryName = "Women", Currency = "USD" };

        SeedLoader.ToProduct(row).Id.Should().Be(SeedLoader.ToProduct(row).Id);
    }

    [Fact]
    public async Task LoadAsync_ParsesEveryRow_AndUpsertsEachOnce()
    {
        var csv = string.Join("\n",
            Header,
            "MEN-SHIR-001,Arrow Classic Shirts,Premium shirts,Arrow,Men,Shirts,1299.99,USD,10,S|M|L,Black|White,Cotton",
            "KID-CAPS-003,\"Babyhug Cap, soft\",Comfy cap,Babyhug,Kids,Caps,199.99,USD,0,One Size,Red,Cotton");

        var captured = new List<ProductEntity>();
        var sink = new Mock<IProductUpsertSink>();
        sink.Setup(s => s.UpsertAsync(It.IsAny<ProductEntity>(), It.IsAny<CancellationToken>()))
            .Callback<ProductEntity, CancellationToken>((p, _) => captured.Add(p))
            .Returns(Task.CompletedTask);

        var loader = new SeedLoader(sink.Object, NullLogger<SeedLoader>.Instance);
        var count = await loader.LoadAsync(new StringReader(csv));

        count.Should().Be(2);
        sink.Verify(s => s.UpsertAsync(It.IsAny<ProductEntity>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        captured.Select(p => p.SKU).Should().Equal("MEN-SHIR-001", "KID-CAPS-003");
        captured.Select(p => p.Id).Should().Equal(
            DeterministicId.FromSku("MEN-SHIR-001"), DeterministicId.FromSku("KID-CAPS-003"));
        // The quoted field containing a comma is parsed as a single value.
        captured[1].Name.Should().Be("Babyhug Cap, soft");
        captured[0].Sizes.Should().Equal("S", "M", "L");
    }

    [Fact]
    public async Task LoadAsync_RunTwice_ProducesSameIds_NoDuplicateIds()
    {
        var csv = string.Join("\n",
            Header,
            "MEN-SHIR-001,Arrow Shirt,desc,Arrow,Men,Shirts,1299.99,USD,10,S|M,Black,Cotton");

        var first = new List<ProductEntity>();
        var second = new List<ProductEntity>();

        var loader1 = new SeedLoader(SinkInto(first), NullLogger<SeedLoader>.Instance);
        var loader2 = new SeedLoader(SinkInto(second), NullLogger<SeedLoader>.Instance);

        await loader1.LoadAsync(new StringReader(csv));
        await loader2.LoadAsync(new StringReader(csv));

        // Same SKU -> same deterministic id on every run, so an upsert converges (no duplicates).
        second[0].Id.Should().Be(first[0].Id);
    }

    private static IProductUpsertSink SinkInto(List<ProductEntity> bucket)
    {
        var sink = new Mock<IProductUpsertSink>();
        sink.Setup(s => s.UpsertAsync(It.IsAny<ProductEntity>(), It.IsAny<CancellationToken>()))
            .Callback<ProductEntity, CancellationToken>((p, _) => bucket.Add(p))
            .Returns(Task.CompletedTask);
        return sink.Object;
    }
}
