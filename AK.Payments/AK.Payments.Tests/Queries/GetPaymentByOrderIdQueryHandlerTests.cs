using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Application.Queries.GetPaymentByOrderId;
using AK.Payments.Tests.TestData;
using FluentAssertions;
using Moq;

namespace AK.Payments.Tests.Queries;

public sealed class GetPaymentByOrderIdQueryHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IPaymentRepository> _payments = new();

    public GetPaymentByOrderIdQueryHandlerTests()
    {
        _uow.Setup(u => u.Payments).Returns(_payments.Object);
    }

    private GetPaymentByOrderIdQueryHandler CreateHandler() => new(_uow.Object);

    [Fact]
    public async Task Handle_WhenPaymentExists_ReturnsPaymentDto()
    {
        var payment = PaymentTestDataFactory.CreatePayment();
        _payments.Setup(r => r.GetByOrderIdAsync(payment.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var result = await CreateHandler().Handle(new GetPaymentByOrderIdQuery(payment.OrderId), CancellationToken.None);

        result.Should().NotBeNull();
        result!.OrderId.Should().Be(payment.OrderId);
    }

    [Fact]
    public async Task Handle_WhenNoPaymentForOrder_ReturnsNull()
    {
        _payments.Setup(r => r.GetByOrderIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AK.Payments.Domain.Entities.Payment?)null);

        var result = await CreateHandler().Handle(new GetPaymentByOrderIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PassesOrderIdToRepository()
    {
        var orderId = Guid.NewGuid();
        _payments.Setup(r => r.GetByOrderIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AK.Payments.Domain.Entities.Payment?)null);

        await CreateHandler().Handle(new GetPaymentByOrderIdQuery(orderId), CancellationToken.None);

        _payments.Verify(r => r.GetByOrderIdAsync(orderId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
