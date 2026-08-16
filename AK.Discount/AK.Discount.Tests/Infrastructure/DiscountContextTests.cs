using AK.Discount.Infrastructure.Persistence;
using AK.Discount.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AK.Discount.Tests.Infrastructure;

public sealed class DiscountContextTests
{
    private static DbContextOptions<DiscountContext> GetOptions() =>
        new DbContextOptionsBuilder<DiscountContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    [Fact]
    public void DiscountContext_CanBeCreatedWithInMemoryDatabase()
    {
        using var ctx = new DiscountContext(GetOptions());
        ctx.Should().NotBeNull();
        ctx.Coupons.Should().NotBeNull();
    }

    [Fact]
    public async Task DiscountContext_CanAddAndQueryCoupon()
    {
        var opts = GetOptions();
        using var ctx = new DiscountContext(opts);
        var coupon = TestDataFactory.CreateCoupon("MEN-SHIR-001", 1);
        ctx.Coupons.Add(coupon);
        await ctx.SaveChangesAsync();

        ctx.Coupons.Should().HaveCount(1);
        ctx.Coupons.First().ProductId.Should().Be("MEN-SHIR-001");
    }

    [Fact]
    public async Task DiscountContext_CanAddMultipleCoupons()
    {
        var opts = GetOptions();
        using var ctx = new DiscountContext(opts);
        ctx.Coupons.AddRange(
            TestDataFactory.CreateCoupon("SKU-001", 1),
            TestDataFactory.CreateCoupon("SKU-002", 2),
            TestDataFactory.CreateCoupon("SKU-003", 3)
        );
        await ctx.SaveChangesAsync();

        ctx.Coupons.Count().Should().Be(3);
    }

    [Fact]
    public async Task DiscountContext_CanRemoveCoupon()
    {
        var opts = GetOptions();
        using var ctx = new DiscountContext(opts);
        var coupon = TestDataFactory.CreateCoupon("MEN-SHIR-001", 1);
        ctx.Coupons.Add(coupon);
        await ctx.SaveChangesAsync();

        ctx.Coupons.Remove(coupon);
        await ctx.SaveChangesAsync();

        ctx.Coupons.Should().BeEmpty();
    }
}
