using AK.Order.Application.Features.UpdateOrderStatus;
using AK.Order.Domain.Enums;
using FluentAssertions;

namespace AK.Order.Tests.Features.UpdateOrderStatus;

public class UpdateOrderStatusValidatorTests
{
    private readonly UpdateOrderStatusValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        var result = _validator.Validate(new UpdateOrderStatusCommand(Guid.NewGuid(), OrderStatus.Processing));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyOrderId_Fails()
    {
        var result = _validator.Validate(new UpdateOrderStatusCommand(Guid.Empty, OrderStatus.Processing));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_InvalidStatus_Fails()
    {
        var result = _validator.Validate(new UpdateOrderStatusCommand(Guid.NewGuid(), (OrderStatus)999));
        result.IsValid.Should().BeFalse();
    }
}
