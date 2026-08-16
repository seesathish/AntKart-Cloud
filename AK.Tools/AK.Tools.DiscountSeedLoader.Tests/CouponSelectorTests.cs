using AK.Discount.Domain.Enums;
using AK.Tools.DiscountSeedLoader;
using FluentAssertions;

namespace AK.Tools.DiscountSeedLoader.Tests;

public sealed class CouponSelectorTests
{
    private static readonly DateTime Now = new(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc);

    private static SeedProduct Product(string sku, string category, string? sub, decimal price = 100m) =>
        new(Id: $"id-{sku}", Name: $"Name {sku}", Sku: sku, CategoryName: category, SubCategoryName: sub,
            Price: price, Currency: "USD");

    [Fact]
    public void Kids_Product_Gets_15Percent_ByCategoryRule()
    {
        var coupons = CouponSelector.Select([Product("KID-FROC-001", "Kids", "Frocks")], Now);

        coupons.Should().ContainSingle();
        var c = coupons[0];
        c.Rule.Should().Be(SelectionRule.KidsCategory);
        c.Coupon.Amount.Should().Be(15m);
        c.Coupon.DiscountType.Should().Be(DiscountType.Percentage);
        c.Coupon.ProductId.Should().Be("id-KID-FROC-001");
        c.Coupon.CouponCode.Should().Be("SAVE-KID-FROC-001");
    }

    [Fact]
    public void MenShirts_Product_Gets_10Percent_ByCategoryRule()
    {
        var coupons = CouponSelector.Select([Product("MEN-SHIR-001", "Men", "Shirts")], Now);

        coupons.Should().ContainSingle();
        coupons[0].Rule.Should().Be(SelectionRule.MenShirts);
        coupons[0].Coupon.Amount.Should().Be(10m);
        coupons[0].Coupon.DiscountType.Should().Be(DiscountType.Percentage);
    }

    [Fact]
    public void RuleB_TakesPrecedence_OverRuleA_EvenWhenHashWouldSelect()
    {
        // A Men/Shirts product whose hash WOULD qualify for Rule A must still get the Rule B 10%.
        var menShirts = Enumerable.Range(1, 50)
            .Select(i => Product($"MEN-SHIR-{i:D3}", "Men", "Shirts"))
            .First(p => SkuHash.Of(p.Sku) % 5 == 0);

        var coupons = CouponSelector.Select([menShirts], Now);

        coupons[0].Rule.Should().Be(SelectionRule.MenShirts);
        coupons[0].Coupon.Amount.Should().Be(10m);
    }

    [Fact]
    public void RuleA_Selects_Exactly_The_HashMod5_Zero_Products_AmongRemaining()
    {
        // Women/Dresses are caught by neither Rule B branch, so they fall to Rule A.
        var products = Enumerable.Range(1, 40)
            .Select(i => Product($"WOM-DRES-{i:D3}", "Women", "Dresses"))
            .ToList();

        var expectedSkus = products.Where(p => SkuHash.Of(p.Sku) % 5 == 0).Select(p => p.Sku).ToHashSet();

        var coupons = CouponSelector.Select(products, Now);

        coupons.Should().OnlyContain(c => c.Rule == SelectionRule.DeterministicSpread);
        coupons.Select(c => c.Product.Sku).Should().BeEquivalentTo(expectedSkus);
    }

    [Fact]
    public void RuleA_DiscountVariants_AreFromTheKnownSet()
    {
        var products = Enumerable.Range(1, 60).Select(i => Product($"WOM-TOPS-{i:D3}", "Women", "Tops")).ToList();

        var coupons = CouponSelector.Select(products, Now);

        coupons.Should().NotBeEmpty();
        foreach (var c in coupons)
        {
            (c.Coupon.Amount, c.Coupon.DiscountType).Should().BeOneOf(
                (10m, DiscountType.Percentage),
                (20m, DiscountType.Percentage),
                (25m, DiscountType.Percentage),
                (5m, DiscountType.FlatAmount),
                (10m, DiscountType.FlatAmount));
        }
    }

    [Fact]
    public void Selection_IsDeterministic_AcrossRuns()
    {
        var products = new[]
        {
            Product("KID-FROC-002", "Kids", "Frocks"),
            Product("MEN-SHIR-009", "Men", "Shirts"),
            Product("WOM-DRES-005", "Women", "Dresses"),
            Product("WOM-DRES-010", "Women", "Dresses"),
            Product("MEN-JEAN-003", "Men", "Jeans"),
        };

        var first = CouponSelector.Select(products, Now);
        var second = CouponSelector.Select(products, Now);

        first.Select(c => (c.Coupon.ProductId, c.Coupon.Amount, c.Coupon.DiscountType, c.Coupon.CouponCode))
            .Should().Equal(second.Select(c => (c.Coupon.ProductId, c.Coupon.Amount, c.Coupon.DiscountType, c.Coupon.CouponCode)));
    }

    [Fact]
    public void EveryProduct_GetsAtMostOneCoupon()
    {
        var products = Enumerable.Range(1, 100)
            .Select(i => Product($"WOM-DRES-{i:D3}", "Women", "Dresses"))
            .Concat(Enumerable.Range(1, 20).Select(i => Product($"KID-FROC-{i:D3}", "Kids", "Frocks")))
            .ToList();

        var coupons = CouponSelector.Select(products, Now);

        coupons.Select(c => c.Coupon.ProductId).Should().OnlyHaveUniqueItems();
        coupons.Count.Should().BeLessThanOrEqualTo(products.Count);
    }

    [Fact]
    public void Coupon_HasOneYearValidity_Active_AndMinQuantityOne()
    {
        var c = CouponSelector.Select([Product("KID-FROC-001", "Kids", "Frocks")], Now)[0].Coupon;

        c.ValidFrom.Should().Be(Now);
        c.ValidTo.Should().Be(Now.AddYears(1));
        c.IsActive.Should().BeTrue();
        c.MinimumQuantity.Should().Be(1);
    }
}
