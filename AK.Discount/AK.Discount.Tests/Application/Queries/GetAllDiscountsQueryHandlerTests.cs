using AK.Discount.Application.Interfaces;
using AK.Discount.Application.Queries.GetAllDiscounts;
using AK.Discount.Domain.Entities;
using AK.Discount.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.Discount.Tests.Application.Queries;

public sealed class GetAllDiscountsQueryHandlerTests
{
    private readonly Mock<ICouponRepository> _repoMock = new();
    private readonly GetAllDiscountsQueryHandler _handler;

    public GetAllDiscountsQueryHandlerTests() => _handler = new(_repoMock.Object);

    [Fact]
    public async Task Handle_WithDefaultPaging_ShouldReturnPagedResult()
    {
        var coupons = new List<Coupon> { TestDataFactory.CreateCoupon() };
        _repoMock.Setup(r => r.GetAllAsync(1, 20, default)).ReturnsAsync(coupons);
        _repoMock.Setup(r => r.GetTotalCountAsync(default)).ReturnsAsync(1);

        var result = await _handler.Handle(new GetAllDiscountsQuery(), default);

        result.Items.Should().HaveCount(1);
        result.TotalCount.Should().Be(1);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task Handle_WithCustomPaging_ShouldPassCorrectParameters()
    {
        var coupons = new List<Coupon>
        {
            TestDataFactory.CreateCoupon("SKU-001", 1),
            TestDataFactory.CreateCoupon("SKU-002", 2)
        };
        _repoMock.Setup(r => r.GetAllAsync(2, 10, default)).ReturnsAsync(coupons);
        _repoMock.Setup(r => r.GetTotalCountAsync(default)).ReturnsAsync(25);

        var result = await _handler.Handle(new GetAllDiscountsQuery(Page: 2, PageSize: 10), default);

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(25);
        result.Page.Should().Be(2);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task Handle_WithPageLessThanOne_ShouldNormalizeTo1()
    {
        _repoMock.Setup(r => r.GetAllAsync(1, 20, default)).ReturnsAsync(new List<Coupon>());
        _repoMock.Setup(r => r.GetTotalCountAsync(default)).ReturnsAsync(0);

        var result = await _handler.Handle(new GetAllDiscountsQuery(Page: 0), default);

        result.Page.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithPageSizeAbove100_ShouldClampTo100()
    {
        _repoMock.Setup(r => r.GetAllAsync(1, 100, default)).ReturnsAsync(new List<Coupon>());
        _repoMock.Setup(r => r.GetTotalCountAsync(default)).ReturnsAsync(0);

        var result = await _handler.Handle(new GetAllDiscountsQuery(Page: 1, PageSize: 999), default);

        result.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task Handle_WithEmptyResult_ShouldReturnEmptyPagedResult()
    {
        _repoMock.Setup(r => r.GetAllAsync(1, 20, default)).ReturnsAsync(new List<Coupon>());
        _repoMock.Setup(r => r.GetTotalCountAsync(default)).ReturnsAsync(0);

        var result = await _handler.Handle(new GetAllDiscountsQuery(), default);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShouldMapCouponsToDtos()
    {
        var coupon = TestDataFactory.CreateCoupon("MEN-SHIR-001", 1);
        _repoMock.Setup(r => r.GetAllAsync(1, 20, default))
            .ReturnsAsync(new List<Coupon> { coupon });
        _repoMock.Setup(r => r.GetTotalCountAsync(default)).ReturnsAsync(1);

        var result = await _handler.Handle(new GetAllDiscountsQuery(), default);

        result.Items[0].ProductId.Should().Be(coupon.ProductId);
        result.Items[0].Amount.Should().Be(coupon.Amount);
    }
}
