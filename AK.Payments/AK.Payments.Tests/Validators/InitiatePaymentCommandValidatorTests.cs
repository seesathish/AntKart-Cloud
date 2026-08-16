using AK.Payments.Application.Commands.InitiatePayment;
using AK.Payments.Domain.Enums;
using FluentAssertions;

namespace AK.Payments.Tests.Validators;

public sealed class InitiatePaymentCommandValidatorTests
{
    private readonly InitiatePaymentCommandValidator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_Passes()
    {
        var command = new InitiatePaymentCommand(Guid.NewGuid(), "user1", "user@test.com", "Test User", "ORD-001", 100m, PaymentMethod.Card);
        var result = _validator.Validate(command);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithEmptyOrderId_Fails()
    {
        var command = new InitiatePaymentCommand(Guid.Empty, "user1", "user@test.com", "Test User", "ORD-001", 100m, PaymentMethod.Card);
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "OrderId");
    }

    [Fact]
    public void Validate_WithEmptyUserId_Fails()
    {
        var command = new InitiatePaymentCommand(Guid.NewGuid(), string.Empty, "user@test.com", "Test User", "ORD-001", 100m, PaymentMethod.Card);
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "UserId");
    }

    [Fact]
    public void Validate_WithZeroAmount_Fails()
    {
        var command = new InitiatePaymentCommand(Guid.NewGuid(), "user1", "user@test.com", "Test User", "ORD-001", 0m, PaymentMethod.Card);
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Amount");
    }

    [Fact]
    public void Validate_WithEmptyOrderNumber_Fails()
    {
        var command = new InitiatePaymentCommand(Guid.NewGuid(), "user1", "user@test.com", "Test User", string.Empty, 100m, PaymentMethod.Card);
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "OrderNumber");
    }
}
