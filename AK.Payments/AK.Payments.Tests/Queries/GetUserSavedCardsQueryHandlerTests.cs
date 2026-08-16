using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Application.Queries.GetUserSavedCards;
using AK.Payments.Tests.TestData;
using FluentAssertions;
using Moq;

namespace AK.Payments.Tests.Queries;

public sealed class GetUserSavedCardsQueryHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ISavedCardRepository> _cards = new();

    public GetUserSavedCardsQueryHandlerTests()
    {
        _uow.Setup(u => u.SavedCards).Returns(_cards.Object);
    }

    private GetUserSavedCardsQueryHandler CreateHandler() => new(_uow.Object);

    [Fact]
    public async Task Handle_ReturnsSavedCardDtosForUser()
    {
        var card = PaymentTestDataFactory.CreateSavedCard("user1");
        _cards.Setup(r => r.GetByUserIdAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AK.Payments.Domain.Entities.SavedCard> { card });

        var result = await CreateHandler().Handle(new GetUserSavedCardsQuery("user1"), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].UserId.Should().Be("user1");
        result[0].RazorpayTokenId.Should().Be(card.RazorpayTokenId);
    }

    [Fact]
    public async Task Handle_WhenNoSavedCards_ReturnsEmptyList()
    {
        _cards.Setup(r => r.GetByUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AK.Payments.Domain.Entities.SavedCard>());

        var result = await CreateHandler().Handle(new GetUserSavedCardsQuery("user1"), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PassesUserIdToRepository()
    {
        _cards.Setup(r => r.GetByUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AK.Payments.Domain.Entities.SavedCard>());

        await CreateHandler().Handle(new GetUserSavedCardsQuery("user-abc"), CancellationToken.None);

        _cards.Verify(r => r.GetByUserIdAsync("user-abc", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MapsDtoFieldsCorrectly()
    {
        var card = PaymentTestDataFactory.CreateSavedCard("user1");
        _cards.Setup(r => r.GetByUserIdAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AK.Payments.Domain.Entities.SavedCard> { card });

        var result = await CreateHandler().Handle(new GetUserSavedCardsQuery("user1"), CancellationToken.None);

        var dto = result[0];
        dto.CardNetwork.Should().Be(card.CardNetwork);
        dto.Last4.Should().Be(card.Last4);
        dto.CardType.Should().Be(card.CardType);
        dto.CardName.Should().Be(card.CardName);
        dto.IsDefault.Should().Be(card.IsDefault);
    }
}
