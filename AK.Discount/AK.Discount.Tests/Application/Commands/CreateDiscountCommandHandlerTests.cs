using AK.Discount.Application.Commands.CreateDiscount;
using AK.Discount.Application.Interfaces;
using AK.Discount.Domain.Entities;
using AK.Discount.Tests.Common;
using FluentAssertions;
using Moq;
namespace AK.Discount.Tests.Application.Commands;
public class CreateDiscountCommandHandlerTests
{
    private readonly Mock<ICouponRepository> _repoMock = new();
    private readonly CreateDiscountCommandHandler _handler;
    public CreateDiscountCommandHandlerTests() => _handler = new(_repoMock.Object);

    [Fact]
    public async Task Handle_WithValidCommand_ShouldCreateAndReturnCouponDto()
    {
        var dto = TestDataFactory.CreateCouponDto();
        _repoMock.Setup(r => r.CouponCodeExistsAsync("SAVE10", default)).ReturnsAsync(false);
        _repoMock.Setup(r => r.CreateAsync(It.IsAny<Coupon>(), default))
            .ReturnsAsync((Coupon c, CancellationToken _) => { c.Id = 1; return c; });
        var result = await _handler.Handle(new CreateDiscountCommand(dto), default);
        result.Should().NotBeNull();
        result.ProductId.Should().Be(dto.ProductId);
        result.CouponCode.Should().Be("SAVE10");
    }

    [Fact]
    public async Task Handle_WithDuplicateCouponCode_ShouldThrowInvalidOperationException()
    {
        var dto = TestDataFactory.CreateCouponDto();
        _repoMock.Setup(r => r.CouponCodeExistsAsync("SAVE10", default)).ReturnsAsync(true);
        var act = () => _handler.Handle(new CreateDiscountCommand(dto), default);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*SAVE10*");
    }
}
