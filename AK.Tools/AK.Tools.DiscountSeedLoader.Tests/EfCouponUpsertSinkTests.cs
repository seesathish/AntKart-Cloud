using AK.Discount.Domain.Entities;
using AK.Discount.Domain.Enums;
using AK.Discount.Infrastructure.Persistence;
using AK.Tools.DiscountSeedLoader;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AK.Tools.DiscountSeedLoader.Tests;

public sealed class EfCouponUpsertSinkTests
{
    private static DiscountContext NewContext() =>
        new(new DbContextOptionsBuilder<DiscountContext>()
            .UseInMemoryDatabase($"discount-{Guid.NewGuid():N}")
            .Options);

    private static Coupon Coupon(string productId, decimal amount) => new()
    {
        ProductId = productId,
        ProductName = "Some Product",
        CouponCode = $"SAVE-{productId}",
        Description = $"{amount}% off",
        Amount = amount,
        DiscountType = DiscountType.Percentage,
        ValidFrom = DateTime.UtcNow,
        ValidTo = DateTime.UtcNow.AddYears(1),
        IsActive = true,
        MinimumQuantity = 1
    };

    [Fact]
    public async Task Upsert_InsertsWhenAbsent()
    {
        using var ctx = NewContext();
        var sink = new EfCouponUpsertSink(ctx);

        await sink.UpsertAsync(Coupon("prod-1", 10m));

        ctx.Coupons.Should().ContainSingle(c => c.ProductId == "prod-1");
    }

    [Fact]
    public async Task Upsert_SameProductTwice_ConvergesToOneRow_NoDuplicates()
    {
        using var ctx = NewContext();
        var sink = new EfCouponUpsertSink(ctx);

        await sink.UpsertAsync(Coupon("prod-1", 10m));
        await sink.UpsertAsync(Coupon("prod-1", 25m)); // re-run with a changed discount

        var rows = await ctx.Coupons.Where(c => c.ProductId == "prod-1").ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Amount.Should().Be(25m); // updated in place
    }
}
