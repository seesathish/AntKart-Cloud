using AK.Discount.Domain.Entities;
using AK.Tools.DiscountSeedLoader;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AK.Tools.DiscountSeedLoader.Tests;

public sealed class DiscountSeedLoaderTests
{
    // Records the order of operations so we can assert Clear happens before any seed write.
    private sealed class RecordingSink(int clearedCount) : ICouponUpsertSink
    {
        public List<string> Ops { get; } = [];
        public int Upserts { get; private set; }

        public Task<int> ClearAsync(CancellationToken ct = default)
        {
            Ops.Add("clear");
            return Task.FromResult(clearedCount);
        }

        public Task UpsertAsync(Coupon coupon, CancellationToken ct = default)
        {
            Ops.Add("upsert");
            Upserts++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProductSource(IReadOnlyList<SeedProduct> products) : IProductSource
    {
        public Task<IReadOnlyList<SeedProduct>> GetProductsAsync(CancellationToken ct = default) =>
            Task.FromResult(products);
    }

    private static SeedProduct Product(string sku, string category, string? sub) =>
        new($"id-{sku}", $"Name {sku}", sku, category, sub, 100m, "USD");

    [Fact]
    public async Task RunAsync_ClearsBeforeSeeding()
    {
        var sink = new RecordingSink(clearedCount: 7);
        var source = new FakeProductSource([Product("KID-FROC-001", "Kids", "Frocks"), Product("MEN-SHIR-001", "Men", "Shirts")]);
        var loader = new DiscountSeedLoader(source, sink, NullLogger<DiscountSeedLoader>.Instance);

        await loader.RunAsync();

        sink.Ops.Should().NotBeEmpty();
        sink.Ops[0].Should().Be("clear");                       // clear is the very first operation
        sink.Ops.Count(o => o == "clear").Should().Be(1);       // cleared exactly once
        sink.Ops.IndexOf("clear").Should().BeLessThan(sink.Ops.IndexOf("upsert")); // before any write
    }

    [Fact]
    public async Task RunAsync_Summary_ReportsClearedCount_AndSeededCoupons()
    {
        var sink = new RecordingSink(clearedCount: 42);
        var source = new FakeProductSource([Product("KID-FROC-001", "Kids", "Frocks"), Product("MEN-SHIR-001", "Men", "Shirts")]);
        var loader = new DiscountSeedLoader(source, sink, NullLogger<DiscountSeedLoader>.Instance);

        var summary = await loader.RunAsync();

        summary.CouponsCleared.Should().Be(42);
        summary.ProductsRead.Should().Be(2);
        summary.KidsCoupons.Should().Be(1);
        summary.MenShirtsCoupons.Should().Be(1);
        summary.TotalCoupons.Should().Be(sink.Upserts);
    }
}
