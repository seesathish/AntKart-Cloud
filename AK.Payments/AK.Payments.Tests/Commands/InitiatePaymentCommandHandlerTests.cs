using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Payments.Application.Commands.InitiatePayment;
using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Application.DTOs;
using AK.Payments.Domain.Entities;
using AK.Payments.Domain.Enums;
using AK.Payments.Tests.TestData;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Moq;

namespace AK.Payments.Tests.Commands;

public sealed class InitiatePaymentCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IRazorpayClient> _razorpay = new();
    private readonly Mock<IPublishEndpoint> _publisher = new();
    private readonly Mock<IConfiguration> _config = new();

    public InitiatePaymentCommandHandlerTests()
    {
        _uow.Setup(u => u.Payments).Returns(_payments.Object);
        _razorpay.Setup(r => r.CreateOrderAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RazorpayOrderResponse("order_test123", "created", 99900L, "INR", "receipt"));
        _config.Setup(c => c["Razorpay:KeyId"]).Returns("rzp_test_key");
    }

    private InitiatePaymentCommandHandler CreateHandler()
        => new(_uow.Object, _razorpay.Object, _publisher.Object, _config.Object);

    private static InitiatePaymentCommand BuildCommand(
        Guid? orderId = null,
        string userId = "user1",
        string email = "user1@test.com",
        string name = "Test User",
        string orderNumber = "ORD-20260425-TESTTEST",
        decimal amount = 999m)
        => new(orderId ?? PaymentTestDataFactory.OrderId1, userId, email, name, orderNumber, amount, PaymentMethod.Card);

    [Fact]
    public async Task Handle_WithValidCommand_CreatesPaymentAndReturnsResponse()
    {
        var result = await CreateHandler().Handle(BuildCommand(), CancellationToken.None);

        result.Should().NotBeNull();
        result.RazorpayOrderId.Should().Be("order_test123");
        result.RazorpayKeyId.Should().Be("rzp_test_key");
        result.Amount.Should().Be(999m);
        result.Currency.Should().Be("INR");
    }

    [Fact]
    public async Task Handle_PublishesPaymentInitiatedIntegrationEvent()
    {
        await CreateHandler().Handle(BuildCommand(), CancellationToken.None);

        _publisher.Verify(p => p.Publish(It.IsAny<PaymentInitiatedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_StoresCustomerEmailAndOrderNumber()
    {
        Payment? captured = null;
        _payments.Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .Callback<Payment, CancellationToken>((p, _) => captured = p);

        await CreateHandler().Handle(BuildCommand(email: "cust@antkart.com", orderNumber: "ORD-20260425-ABCD1234"), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.CustomerEmail.Should().Be("cust@antkart.com");
        captured.OrderNumber.Should().Be("ORD-20260425-ABCD1234");
    }

    [Fact]
    public async Task Handle_SetsRazorpayOrderIdOnPayment()
    {
        Payment? captured = null;
        _payments.Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .Callback<Payment, CancellationToken>((p, _) => captured = p);

        await CreateHandler().Handle(BuildCommand(), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.RazorpayOrderId.Should().Be("order_test123");
        captured.Status.Should().Be(PaymentStatus.Initiated);
    }

    [Fact]
    public async Task Handle_CallsRazorpayCreateOrderWithCorrectAmount()
    {
        await CreateHandler().Handle(BuildCommand(amount: 500m), CancellationToken.None);

        _razorpay.Verify(r => r.CreateOrderAsync(500m, "INR", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SavesChanges()
    {
        await CreateHandler().Handle(BuildCommand(), CancellationToken.None);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
