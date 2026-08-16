using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Application.Queries.GetPaymentById;
using AK.Payments.Tests.TestData;
using FluentAssertions;
using Moq;

namespace AK.Payments.Tests.Queries;

public sealed class GetPaymentByIdQueryHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IPaymentRepository> _payments = new();

    public GetPaymentByIdQueryHandlerTests()
    {
        _uow.Setup(u => u.Payments).Returns(_payments.Object);
    }

    private GetPaymentByIdQueryHandler CreateHandler() => new(_uow.Object);

    [Fact]
    public async Task Handle_WhenPaymentExists_ReturnsPaymentDto()
    {
        var payment = PaymentTestDataFactory.CreatePayment();
        _payments.Setup(r => r.GetByIdAsync(payment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var result = await CreateHandler().Handle(new GetPaymentByIdQuery(payment.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(payment.Id);
        result.OrderId.Should().Be(payment.OrderId);
        result.UserId.Should().Be(payment.UserId);
        result.Amount.Should().Be(payment.Amount);
    }

    [Fact]
    public async Task Handle_WhenPaymentNotFound_ReturnsNull()
    {
        _payments.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AK.Payments.Domain.Entities.Payment?)null);

        var result = await CreateHandler().Handle(new GetPaymentByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MapsStatusAndMethodToString()
    {
        var payment = PaymentTestDataFactory.CreatePayment();
        _payments.Setup(r => r.GetByIdAsync(payment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var result = await CreateHandler().Handle(new GetPaymentByIdQuery(payment.Id), CancellationToken.None);

        result!.Status.Should().NotBeNullOrEmpty();
        result.Method.Should().NotBeNullOrEmpty();
    }
}
