using AK.Discount.Application.Commands.DeleteDiscount;
using AK.Discount.Application.Interfaces;
using AK.Discount.Domain.Entities;
using AK.Discount.Tests.Common;
using FluentAssertions;
using Moq;
namespace AK.Discount.Tests.Application.Commands;
public class DeleteDiscountCommandHandlerTests
{
    private readonly Mock<ICouponRepository> _repoMock = new();
    private readonly DeleteDiscountCommandHandler _handler;
    public DeleteDiscountCommandHandlerTests() => _handler = new(_repoMock.Object);

    [Fact]
    public async Task Handle_WithExistingId_ShouldReturnTrue()
    {
        var coupon = TestDataFactory.CreateCoupon();
        _repoMock.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(coupon);
        _repoMock.Setup(r => r.DeleteAsync(1, default)).ReturnsAsync(true);
        var result = await _handler.Handle(new DeleteDiscountCommand(1), default);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithNonExistentId_ShouldThrowKeyNotFoundException()
    {
        _repoMock.Setup(r => r.GetByIdAsync(999, default)).ReturnsAsync((Coupon?)null);
        var act = () => _handler.Handle(new DeleteDiscountCommand(999), default);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
