using AK.Discount.Application.Commands.UpdateDiscount;
using AK.Discount.Application.Interfaces;
using AK.Discount.Domain.Entities;
using AK.Discount.Tests.Common;
using FluentAssertions;
using Moq;
namespace AK.Discount.Tests.Application.Commands;
public class UpdateDiscountCommandHandlerTests
{
    private readonly Mock<ICouponRepository> _repoMock = new();
    private readonly UpdateDiscountCommandHandler _handler;
    public UpdateDiscountCommandHandlerTests() => _handler = new(_repoMock.Object);

    [Fact]
    public async Task Handle_WithExistingId_ShouldUpdateAndReturnDto()
    {
        var coupon = TestDataFactory.CreateCoupon();
        var updateDto = TestDataFactory.UpdateCouponDto();
        _repoMock.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(coupon);
        _repoMock.Setup(r => r.UpdateAsync(coupon, default)).ReturnsAsync(coupon);
        var result = await _handler.Handle(new UpdateDiscountCommand(1, updateDto), default);
        result.Should().NotBeNull();
        _repoMock.Verify(r => r.UpdateAsync(coupon, default), Times.Once);
    }

    [Fact]
    public async Task Handle_WithNonExistentId_ShouldThrowKeyNotFoundException()
    {
        _repoMock.Setup(r => r.GetByIdAsync(999, default)).ReturnsAsync((Coupon?)null);
        var act = () => _handler.Handle(new UpdateDiscountCommand(999, TestDataFactory.UpdateCouponDto()), default);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
