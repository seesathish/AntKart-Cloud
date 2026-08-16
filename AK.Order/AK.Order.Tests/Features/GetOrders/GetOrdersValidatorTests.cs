using AK.Order.Application.Features.GetOrders;
using FluentAssertions;

namespace AK.Order.Tests.Features.GetOrders;

public class GetOrdersValidatorTests
{
    private readonly GetOrdersValidator _validator = new();

    [Fact]
    public void Validate_DefaultQuery_Passes()
    {
        var result = _validator.Validate(new GetOrdersQuery());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ZeroPage_Fails()
    {
        var result = _validator.Validate(new GetOrdersQuery(Page: 0));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_PageSizeOver100_Fails()
    {
        var result = _validator.Validate(new GetOrdersQuery(PageSize: 101));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ZeroPageSize_Fails()
    {
        var result = _validator.Validate(new GetOrdersQuery(PageSize: 0));
        result.IsValid.Should().BeFalse();
    }
}
