using AK.Order.Application.Features.GetOrdersByUser;
using FluentAssertions;

namespace AK.Order.Tests.Features.GetOrdersByUser;

public class GetOrdersByUserValidatorTests
{
    private readonly GetOrdersByUserValidator _validator = new();

    [Fact]
    public void Validate_ValidQuery_Passes()
    {
        var result = _validator.Validate(new GetOrdersByUserQuery("user-1"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyUserId_Fails()
    {
        var result = _validator.Validate(new GetOrdersByUserQuery(""));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ZeroPage_Fails()
    {
        var result = _validator.Validate(new GetOrdersByUserQuery("user-1", Page: 0));
        result.IsValid.Should().BeFalse();
    }
}
