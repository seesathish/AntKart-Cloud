using AK.Payments.Application.Commands.SaveCard;
using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Application.DTOs;
using AK.Payments.Domain.Entities;
using AK.Payments.Tests.TestData;
using FluentAssertions;
using Moq;

namespace AK.Payments.Tests.Commands;

public sealed class SaveCardCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ISavedCardRepository> _cards = new();
    private readonly Mock<IRazorpayClient> _razorpay = new();

    public SaveCardCommandHandlerTests()
    {
        _uow.Setup(u => u.SavedCards).Returns(_cards.Object);
        _razorpay.Setup(r => r.CreateTokenAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RazorpayTokenResponse("token_abc", "cust_test123", "Visa", "4242", "credit", "John Doe"));
    }

    private SaveCardCommandHandler CreateHandler() => new(_uow.Object, _razorpay.Object);

    private static SaveCardCommand MakeCommand(string userId = "user1")
        => new(userId, "cust_test123", "pay_test456", "John Doe", "john@example.com", "+91-9999999999");

    [Fact]
    public async Task Handle_WithValidCommand_ReturnsSavedCardDto()
    {
        var result = await CreateHandler().Handle(MakeCommand(), CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().BeOfType<SavedCardDto>();
        result.RazorpayTokenId.Should().Be("token_abc");
        result.CardNetwork.Should().Be("Visa");
        result.Last4.Should().Be("4242");
    }

    [Fact]
    public async Task Handle_CallsRazorpayCreateToken()
    {
        await CreateHandler().Handle(MakeCommand(), CancellationToken.None);

        _razorpay.Verify(r => r.CreateTokenAsync("cust_test123", "pay_test456", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SavesCardToRepository()
    {
        await CreateHandler().Handle(MakeCommand(), CancellationToken.None);

        _cards.Verify(r => r.AddAsync(It.IsAny<SavedCard>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SavesChanges()
    {
        await CreateHandler().Handle(MakeCommand(), CancellationToken.None);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_StoredCardHasCorrectUserId()
    {
        SavedCard? captured = null;
        _cards.Setup(r => r.AddAsync(It.IsAny<SavedCard>(), It.IsAny<CancellationToken>()))
            .Callback<SavedCard, CancellationToken>((c, _) => captured = c);

        await CreateHandler().Handle(MakeCommand("user-xyz"), CancellationToken.None);

        captured!.UserId.Should().Be("user-xyz");
    }

    [Fact]
    public async Task Handle_ReturnedDtoReflectsTokenData()
    {
        _razorpay.Setup(r => r.CreateTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RazorpayTokenResponse("token_xyz", "cust_test123", "Mastercard", "1234", "debit", "Jane Doe"));

        var result = await CreateHandler().Handle(MakeCommand(), CancellationToken.None);

        result.CardNetwork.Should().Be("Mastercard");
        result.Last4.Should().Be("1234");
        result.CardType.Should().Be("debit");
        result.CardName.Should().Be("Jane Doe");
    }
}
