using AK.BuildingBlocks.Messaging.EventGrid;
using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.BuildingBlocks.Messaging.Notifications;
using AK.Payments.Application.Commands.VerifyPayment;
using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Domain.Enums;
using AK.Payments.Tests.TestData;
using FluentAssertions;
using MassTransit;
using Moq;

namespace AK.Payments.Tests.Commands;

public sealed class VerifyPaymentCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IRazorpayClient> _razorpay = new();
    private readonly Mock<IPublishEndpoint> _publisher = new();
    private readonly Mock<IEventGridSideEffectPublisher> _sideEffects = new();

    public VerifyPaymentCommandHandlerTests()
    {
        _uow.Setup(u => u.Payments).Returns(_payments.Object);
        _sideEffects.Setup(s => s.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private VerifyPaymentCommandHandler CreateHandler()
        => new(_uow.Object, _razorpay.Object, _publisher.Object, _sideEffects.Object);

    [Fact]
    public async Task Handle_WithValidSignature_MarksPaymentSucceeded()
    {
        var payment = PaymentTestDataFactory.CreatePayment();
        payment.AssignRazorpayOrder("order_123");
        _payments.Setup(r => r.GetByIdAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        _razorpay.Setup(r => r.VerifyPaymentSignature(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        var handler = CreateHandler();
        var result = await handler.Handle(new VerifyPaymentCommand(payment.Id, "order_123", "pay_abc", "sig_abc"), CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Succeeded.ToString());
        result.RazorpayPaymentId.Should().Be("pay_abc");
    }

    [Fact]
    public async Task Handle_WithInvalidSignature_MarksPaymentFailed()
    {
        var payment = PaymentTestDataFactory.CreatePayment();
        payment.AssignRazorpayOrder("order_123");
        _payments.Setup(r => r.GetByIdAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        _razorpay.Setup(r => r.VerifyPaymentSignature(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var handler = CreateHandler();
        var result = await handler.Handle(new VerifyPaymentCommand(payment.Id, "order_123", "pay_abc", "bad_sig"), CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Failed.ToString());
        result.FailureReason.Should().Contain("Signature");
    }

    [Fact]
    public async Task Handle_WithNonExistentPayment_ThrowsKeyNotFoundException()
    {
        _payments.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((AK.Payments.Domain.Entities.Payment?)null);

        var handler = CreateHandler();
        var act = async () => await handler.Handle(new VerifyPaymentCommand(Guid.NewGuid(), "order_123", "pay_abc", "sig_abc"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_PublishesSucceededEventOnSuccess()
    {
        var payment = PaymentTestDataFactory.CreatePayment();
        payment.AssignRazorpayOrder("order_123");
        _payments.Setup(r => r.GetByIdAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        _razorpay.Setup(r => r.VerifyPaymentSignature(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        await CreateHandler().Handle(new VerifyPaymentCommand(payment.Id, "order_123", "pay_abc", "sig_abc"), CancellationToken.None);

        _publisher.Verify(p => p.Publish(It.IsAny<PaymentSucceededIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(p => p.Publish(It.IsAny<PaymentFailedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SucceededEvent_IncludesCustomerEmailAndOrderNumber()
    {
        var payment = PaymentTestDataFactory.CreatePayment(
            customerEmail: "buyer@antkart.com",
            orderNumber: "ORD-20260425-ABCD1234");
        payment.AssignRazorpayOrder("order_123");
        _payments.Setup(r => r.GetByIdAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        _razorpay.Setup(r => r.VerifyPaymentSignature(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        await CreateHandler().Handle(new VerifyPaymentCommand(payment.Id, "order_123", "pay_abc", "sig_abc"), CancellationToken.None);

        _publisher.Verify(p => p.Publish(
            It.Is<PaymentSucceededIntegrationEvent>(e =>
                e.CustomerEmail == "buyer@antkart.com" &&
                e.OrderNumber == "ORD-20260425-ABCD1234" &&
                e.Amount == 999m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PublishesFailedEventOnInvalidSignature()
    {
        var payment = PaymentTestDataFactory.CreatePayment(customerEmail: "buyer@antkart.com");
        payment.AssignRazorpayOrder("order_123");
        _payments.Setup(r => r.GetByIdAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        _razorpay.Setup(r => r.VerifyPaymentSignature(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        await CreateHandler().Handle(new VerifyPaymentCommand(payment.Id, "order_123", "pay_abc", "bad_sig"), CancellationToken.None);

        _publisher.Verify(p => p.Publish(
            It.Is<PaymentFailedIntegrationEvent>(e =>
                e.CustomerEmail == "buyer@antkart.com" &&
                e.Reason.Contains("Signature")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AfterCommit_PublishesPaymentSucceededNotification_OnSuccess()
    {
        var payment = PaymentTestDataFactory.CreatePayment(
            customerEmail: "buyer@antkart.com", orderNumber: "ORD-1");
        payment.AssignRazorpayOrder("order_123");
        _payments.Setup(r => r.GetByIdAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        _razorpay.Setup(r => r.VerifyPaymentSignature(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        PaymentSucceededNotification? published = null;
        _sideEffects.Setup(s => s.TryPublishAsync(
                NotificationEventTypes.PaymentSucceeded, It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, object, CancellationToken>((_, _, data, _) => published = (PaymentSucceededNotification)data)
            .ReturnsAsync(true);

        await CreateHandler().Handle(new VerifyPaymentCommand(payment.Id, "order_123", "pay_abc", "sig_abc"), CancellationToken.None);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        published.Should().NotBeNull();
        published!.CustomerEmail.Should().Be("buyer@antkart.com");
        published.OrderNumber.Should().Be("ORD-1");
        published.PaymentId.Should().Be("pay_abc");
    }

    [Fact]
    public async Task Handle_AfterCommit_PublishesPaymentFailedNotification_OnInvalidSignature()
    {
        var payment = PaymentTestDataFactory.CreatePayment(customerEmail: "buyer@antkart.com");
        payment.AssignRazorpayOrder("order_123");
        _payments.Setup(r => r.GetByIdAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        _razorpay.Setup(r => r.VerifyPaymentSignature(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        await CreateHandler().Handle(new VerifyPaymentCommand(payment.Id, "order_123", "pay_abc", "bad_sig"), CancellationToken.None);

        _sideEffects.Verify(s => s.TryPublishAsync(
            NotificationEventTypes.PaymentFailed, It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        _sideEffects.Verify(s => s.TryPublishAsync(
            NotificationEventTypes.PaymentSucceeded, It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenNotificationPublishFails_DoesNotFailThePayment()
    {
        var payment = PaymentTestDataFactory.CreatePayment();
        payment.AssignRazorpayOrder("order_123");
        _payments.Setup(r => r.GetByIdAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        _razorpay.Setup(r => r.VerifyPaymentSignature(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        _sideEffects.Setup(s => s.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = async () => await CreateHandler().Handle(
            new VerifyPaymentCommand(payment.Id, "order_123", "pay_abc", "sig_abc"), CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.Status.Should().Be(PaymentStatus.Succeeded.ToString());
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
