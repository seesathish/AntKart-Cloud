using AK.Order.Application.Features.CancelOrder;
using FluentAssertions;

namespace AK.Order.Tests.Features.CancelOrder;

public class CancelOrderValidatorTests
{
    private readonly CancelOrderValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        var result = _validator.Validate(new CancelOrderCommand(Guid.NewGuid()));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyOrderId_Fails()
    {
        var result = _validator.Validate(new CancelOrderCommand(Guid.Empty));
        result.IsValid.Should().BeFalse();
    }
}
