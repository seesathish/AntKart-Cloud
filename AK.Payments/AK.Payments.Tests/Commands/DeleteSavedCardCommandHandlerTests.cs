using AK.Payments.Application.Commands.DeleteSavedCard;
using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Tests.TestData;
using FluentAssertions;
using Moq;

namespace AK.Payments.Tests.Commands;

public sealed class DeleteSavedCardCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ISavedCardRepository> _cards = new();
    private readonly Mock<IRazorpayClient> _razorpay = new();

    public DeleteSavedCardCommandHandlerTests()
    {
        _uow.Setup(u => u.SavedCards).Returns(_cards.Object);
    }

    private DeleteSavedCardCommandHandler CreateHandler() => new(_uow.Object, _razorpay.Object);

    [Fact]
    public async Task Handle_WithValidOwner_DeletesCard()
    {
        var card = PaymentTestDataFactory.CreateSavedCard("user1");
        _cards.Setup(r => r.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        await CreateHandler().Handle(new DeleteSavedCardCommand(card.Id, "user1"), CancellationToken.None);

        _cards.Verify(r => r.DeleteAsync(card.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidOwner_CallsRazorpayDeleteToken()
    {
        var card = PaymentTestDataFactory.CreateSavedCard("user1");
        _cards.Setup(r => r.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        await CreateHandler().Handle(new DeleteSavedCardCommand(card.Id, "user1"), CancellationToken.None);

        _razorpay.Verify(r => r.DeleteTokenAsync(card.RazorpayCustomerId, card.RazorpayTokenId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidOwner_SavesChanges()
    {
        var card = PaymentTestDataFactory.CreateSavedCard("user1");
        _cards.Setup(r => r.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        await CreateHandler().Handle(new DeleteSavedCardCommand(card.Id, "user1"), CancellationToken.None);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCardNotFound_ThrowsKeyNotFoundException()
    {
        _cards.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AK.Payments.Domain.Entities.SavedCard?)null);

        var act = () => CreateHandler().Handle(new DeleteSavedCardCommand(Guid.NewGuid(), "user1"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotOwner_ThrowsInvalidOperationException()
    {
        var card = PaymentTestDataFactory.CreateSavedCard("user1");
        _cards.Setup(r => r.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        var act = () => CreateHandler().Handle(new DeleteSavedCardCommand(card.Id, "other-user"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not belong*");
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotOwner_DoesNotDeleteCard()
    {
        var card = PaymentTestDataFactory.CreateSavedCard("user1");
        _cards.Setup(r => r.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        try { await CreateHandler().Handle(new DeleteSavedCardCommand(card.Id, "other-user"), CancellationToken.None); }
        catch (InvalidOperationException) { }

        _cards.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
