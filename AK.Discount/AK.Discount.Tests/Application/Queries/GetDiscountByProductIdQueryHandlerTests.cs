using AK.Discount.Application.Interfaces;
using AK.Discount.Application.Queries.GetDiscountByProductId;
using AK.Discount.Tests.Common;
using FluentAssertions;
using Moq;
namespace AK.Discount.Tests.Application.Queries;
public class GetDiscountByProductIdQueryHandlerTests
{
    private readonly Mock<ICouponRepository> _repoMock = new();
    private readonly GetDiscountByProductIdQueryHandler _handler;
    public GetDiscountByProductIdQueryHandlerTests() => _handler = new(_repoMock.Object);

    [Fact]
    public async Task Handle_WithExistingProductId_ShouldReturnCouponDto()
    {
        var coupon = TestDataFactory.CreateCoupon();
        _repoMock.Setup(r => r.GetByProductIdAsync(coupon.ProductId, default)).ReturnsAsync(coupon);
        var result = await _handler.Handle(new GetDiscountByProductIdQuery(coupon.ProductId), default);
        result.Should().NotBeNull();
        result!.ProductId.Should().Be(coupon.ProductId);
        result.Amount.Should().Be(coupon.Amount);
    }

    [Fact]
    public async Task Handle_WithNonExistentProductId_ShouldReturnNull()
    {
        _repoMock.Setup(r => r.GetByProductIdAsync("NONE", default)).ReturnsAsync((AK.Discount.Domain.Entities.Coupon?)null);
        var result = await _handler.Handle(new GetDiscountByProductIdQuery("NONE"), default);
        result.Should().BeNull();
    }
}
