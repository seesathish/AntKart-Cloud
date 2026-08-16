using AK.Payments.Domain.Entities;
using AK.Payments.Domain.Enums;
using AK.Payments.Tests.TestData;
using FluentAssertions;

namespace AK.Payments.Tests.Domain;

public sealed class PaymentEntityTests
{
    [Fact]
    public void Create_WithValidData_CreatesPayment()
    {
        var payment = PaymentTestDataFactory.CreatePayment();

        payment.Id.Should().NotBeEmpty();
        payment.OrderId.Should().Be(PaymentTestDataFactory.OrderId1);
        payment.UserId.Should().Be(PaymentTestDataFactory.UserId1);
        payment.Amount.Should().Be(999.00m);
        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.Method.Should().Be(PaymentMethod.Card);
        payment.Currency.Should().Be("INR");
    }

    [Fact]
    public void Create_WithZeroAmount_ThrowsArgumentException()
    {
        var act = () => PaymentTestDataFactory.CreatePayment(amount: 0);
        act.Should().Throw<ArgumentException>().WithMessage("*Amount*");
    }

    [Fact]
    public void Create_WithNegativeAmount_ThrowsArgumentException()
    {
        var act = () => PaymentTestDataFactory.CreatePayment(amount: -1);
        act.Should().Throw<ArgumentException>().WithMessage("*Amount*");
    }

    [Fact]
    public void Create_StoresCustomerEmailAndOrderNumber()
    {
        var payment = PaymentTestDataFactory.CreatePayment(
            customerEmail: "test@antkart.com",
            orderNumber: "ORD-20260425-ABCD1234");

        payment.CustomerEmail.Should().Be("test@antkart.com");
        payment.OrderNumber.Should().Be("ORD-20260425-ABCD1234");
    }

    [Fact]
    public void Create_WithEmptyUserId_ThrowsArgumentException()
    {
        var act = () => Payment.Create(Guid.NewGuid(), string.Empty, "a@b.com", "A", "ORD-X", 100m, PaymentMethod.Card);
        act.Should().Throw<ArgumentException>().WithMessage("*UserId*");
    }

    [Fact]
    public void Create_WithEmptyOrderId_ThrowsArgumentException()
    {
        var act = () => Payment.Create(Guid.Empty, "user1", "a@b.com", "A", "ORD-X", 100m, PaymentMethod.Card);
        act.Should().Throw<ArgumentException>().WithMessage("*OrderId*");
    }

    [Fact]
    public void AssignRazorpayOrder_SetsRazorpayOrderIdAndStatusInitiated()
    {
        var payment = PaymentTestDataFactory.CreatePayment();

        payment.AssignRazorpayOrder("order_test123");

        payment.RazorpayOrderId.Should().Be("order_test123");
        payment.Status.Should().Be(PaymentStatus.Initiated);
    }

    [Fact]
    public void MarkSucceeded_SetsStatusToSucceeded()
    {
        var payment = PaymentTestDataFactory.CreatePayment();
        payment.AssignRazorpayOrder("order_test");

        payment.MarkSucceeded("pay_test123", "sig_test");

        payment.Status.Should().Be(PaymentStatus.Succeeded);
        payment.RazorpayPaymentId.Should().Be("pay_test123");
        payment.RazorpaySignature.Should().Be("sig_test");
    }

    [Fact]
    public void MarkSucceeded_WhenAlreadySucceeded_ThrowsInvalidOperation()
    {
        var payment = PaymentTestDataFactory.CreatePayment();
        payment.AssignRazorpayOrder("order_test");
        payment.MarkSucceeded("pay1", "sig1");

        var act = () => payment.MarkSucceeded("pay2", "sig2");
        act.Should().Throw<InvalidOperationException>().WithMessage("*already succeeded*");
    }

    [Fact]
    public void MarkFailed_SetsStatusToFailed()
    {
        var payment = PaymentTestDataFactory.CreatePayment();
        payment.AssignRazorpayOrder("order_test");

        payment.MarkFailed("Signature mismatch");

        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.FailureReason.Should().Be("Signature mismatch");
    }

    [Fact]
    public void MarkFailed_WhenAlreadySucceeded_ThrowsInvalidOperation()
    {
        var payment = PaymentTestDataFactory.CreatePayment();
        payment.AssignRazorpayOrder("order_test");
        payment.MarkSucceeded("pay1", "sig1");

        var act = () => payment.MarkFailed("some reason");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Cannot fail a succeeded payment*");
    }
}
