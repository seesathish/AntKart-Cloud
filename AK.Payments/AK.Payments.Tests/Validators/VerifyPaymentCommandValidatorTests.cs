using AK.Payments.Application.Commands.VerifyPayment;
using FluentAssertions;

namespace AK.Payments.Tests.Validators;

public sealed class VerifyPaymentCommandValidatorTests
{
    private readonly VerifyPaymentCommandValidator _validator = new();

    private static VerifyPaymentCommand ValidCommand() =>
        new(Guid.NewGuid(), "order_test123", "pay_test456", "sig_abc123");

    [Fact]
    public void Validate_WithAllValidFields_Passes()
    {
        var result = _validator.Validate(ValidCommand());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithEmptyPaymentId_Fails()
    {
        var cmd = ValidCommand() with { PaymentId = Guid.Empty };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PaymentId");
    }

    [Fact]
    public void Validate_WithEmptyRazorpayOrderId_Fails()
    {
        var cmd = ValidCommand() with { RazorpayOrderId = string.Empty };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "RazorpayOrderId");
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
    public void Validate_WithEmptyRazorpaySignature_Fails()
    {
        var cmd = ValidCommand() with { RazorpaySignature = string.Empty };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "RazorpaySignature");
    }

    [Fact]
    public void Validate_WithNullRazorpayOrderId_Fails()
    {
        var cmd = ValidCommand() with { RazorpayOrderId = null! };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
    }
}
