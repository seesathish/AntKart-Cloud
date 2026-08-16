using AK.Payments.Application.Commands.SaveCard;
using FluentAssertions;

namespace AK.Payments.Tests.Validators;

public sealed class SaveCardCommandValidatorTests
{
    private readonly SaveCardCommandValidator _validator = new();

    private static SaveCardCommand ValidCommand() =>
        new("user1", "cust_test123", "pay_test456", "John Doe", "john@example.com", "+91-9999999999");

    [Fact]
    public void Validate_WithAllValidFields_Passes()
    {
        var result = _validator.Validate(ValidCommand());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithEmptyUserId_Fails()
    {
        var cmd = ValidCommand() with { UserId = string.Empty };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "UserId");
    }

    [Fact]
    public void Validate_WithEmptyRazorpayCustomerId_Fails()
    {
        var cmd = ValidCommand() with { RazorpayCustomerId = string.Empty };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "RazorpayCustomerId");
    }

    [Fact]
    public void Validate_WithEmptyRazorpayPaymentId_Fails()
    {
        var cmd = ValidCommand() with { RazorpayPaymentId = string.Empty };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "RazorpayPaymentId");
    }

    [Fact]
    public void Validate_WithNullUserId_Fails()
    {
        var cmd = ValidCommand() with { UserId = null! };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
    }
}
