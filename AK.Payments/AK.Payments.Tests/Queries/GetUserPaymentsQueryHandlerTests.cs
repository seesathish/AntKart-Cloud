using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Application.Queries.GetUserPayments;
using AK.Payments.Tests.TestData;
using FluentAssertions;
using Moq;

namespace AK.Payments.Tests.Queries;

public sealed class GetUserPaymentsQueryHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IPaymentRepository> _payments = new();

    public GetUserPaymentsQueryHandlerTests()
    {
        _uow.Setup(u => u.Payments).Returns(_payments.Object);
    }

    private GetUserPaymentsQueryHandler CreateHandler() => new(_uow.Object);

    [Fact]
    public async Task Handle_ReturnsPaymentDtosForUser()
    {
        var p1 = PaymentTestDataFactory.CreatePayment(userId: "user1");
        var p2 = PaymentTestDataFactory.CreatePayment(orderId: Guid.NewGuid(), userId: "user1");
        _payments.Setup(r => r.GetByUserIdAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AK.Payments.Domain.Entities.Payment> { p1, p2 });

        var result = await CreateHandler().Handle(new GetUserPaymentsQuery("user1"), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(p => p.UserId.Should().Be("user1"));
    }

    [Fact]
    public async Task Handle_WhenNoPayments_ReturnsEmptyList()
    {
        _payments.Setup(r => r.GetByUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AK.Payments.Domain.Entities.Payment>());

        var result = await CreateHandler().Handle(new GetUserPaymentsQuery("user1"), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PassesUserIdToRepository()
    {
        _payments.Setup(r => r.GetByUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AK.Payments.Domain.Entities.Payment>());

        await CreateHandler().Handle(new GetUserPaymentsQuery("user-xyz"), CancellationToken.None);

        _payments.Verify(r => r.GetByUserIdAsync("user-xyz", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MapsAllPaymentsToDto()
    {
        var payments = Enumerable.Range(1, 5)
            .Select(i => PaymentTestDataFactory.CreatePayment(orderId: Guid.NewGuid(), amount: i * 100m))
            .ToList();
        _payments.Setup(r => r.GetByUserIdAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payments);

        var result = await CreateHandler().Handle(new GetUserPaymentsQuery("user1"), CancellationToken.None);

        result.Should().HaveCount(5);
    }
}
