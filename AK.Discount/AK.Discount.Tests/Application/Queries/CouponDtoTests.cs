using AK.Discount.Application.DTOs;
using AK.Discount.Application.Interfaces;
using AK.Discount.Application.Queries.GetDiscountByProductId;
using AK.Discount.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.Discount.Tests.Application.Queries;

public sealed class CouponDtoTests
{
    [Fact]
    public async Task GetDiscountByProductId_ShouldMapAllCouponDtoProperties()
    {
        var repoMock = new Mock<ICouponRepository>();
        var coupon = TestDataFactory.CreateCoupon("MEN-SHIR-001", 42);
        repoMock.Setup(r => r.GetByProductIdAsync("MEN-SHIR-001", default)).ReturnsAsync(coupon);
        var handler = new GetDiscountByProductIdQueryHandler(repoMock.Object);

        var result = await handler.Handle(new GetDiscountByProductIdQuery("MEN-SHIR-001"), default);

        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
        result.ProductId.Should().Be("MEN-SHIR-001");
        result.ProductName.Should().Be("Test Shirt");
        result.CouponCode.Should().Be("TEST-001");
        result.Description.Should().Be("Test discount");
        result.Amount.Should().Be(10m);
        result.DiscountType.Should().Be("Percentage");
        result.ValidFrom.Should().BeBefore(DateTime.UtcNow);
        result.ValidTo.Should().BeAfter(DateTime.UtcNow);
        result.IsActive.Should().BeTrue();
        result.MinimumQuantity.Should().Be(1);
    }

    [Fact]
    public void CouponDto_EqualityByValue_ShouldWork()
    {
        var now = DateTime.UtcNow;
        var dto1 = new CouponDto(1, "prod", "Name", "CODE", "Desc", 10m, "Percentage", now, now, true, 1);
        var dto2 = new CouponDto(1, "prod", "Name", "CODE", "Desc", 10m, "Percentage", now, now, true, 1);
        dto1.Should().Be(dto2);
    }
}
