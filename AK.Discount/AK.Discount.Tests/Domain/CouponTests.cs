using AK.Discount.Domain.Entities;
using AK.Discount.Domain.Enums;
using FluentAssertions;
namespace AK.Discount.Tests.Domain;
public class CouponTests
{
    [Fact]
    public void Coupon_DefaultValues_ShouldBeCorrect()
    {
        var coupon = new Coupon();
        coupon.IsActive.Should().BeTrue();
        coupon.MinimumQuantity.Should().Be(1);
    }

    [Fact]
    public void Coupon_WithPercentageDiscount_ShouldSetType()
    {
        var coupon = new Coupon { DiscountType = DiscountType.Percentage, Amount = 20m };
        coupon.DiscountType.Should().Be(DiscountType.Percentage);
        coupon.Amount.Should().Be(20m);
    }

    [Fact]
    public void Coupon_WithFlatDiscount_ShouldSetType()
    {
        var coupon = new Coupon { DiscountType = DiscountType.FlatAmount, Amount = 100m };
        coupon.DiscountType.Should().Be(DiscountType.FlatAmount);
    }
}
