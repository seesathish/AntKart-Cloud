using AK.Payments.Domain.Entities;
using AK.Payments.Tests.TestData;
using FluentAssertions;

namespace AK.Payments.Tests.Domain;

public sealed class SavedCardEntityTests
{
    [Fact]
    public void Create_WithValidData_CreatesSavedCard()
    {
        var card = PaymentTestDataFactory.CreateSavedCard();

        card.Id.Should().NotBeEmpty();
        card.UserId.Should().Be(PaymentTestDataFactory.UserId1);
        card.RazorpayCustomerId.Should().Be("cust_test123");
        card.RazorpayTokenId.Should().Be("token_test123");
        card.CardNetwork.Should().Be("Visa");
        card.Last4.Should().Be("1111");
        card.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void Create_WithEmptyTokenId_ThrowsArgumentException()
    {
        var act = () => SavedCard.Create("user1", "cust_test", string.Empty, "Visa", "1111", "credit", "Test");
        act.Should().Throw<ArgumentException>().WithMessage("*RazorpayTokenId*");
    }

    [Fact]
    public void Create_WithEmptyUserId_ThrowsArgumentException()
    {
        var act = () => SavedCard.Create(string.Empty, "cust_test", "token_test", "Visa", "1111", "credit", "Test");
        act.Should().Throw<ArgumentException>().WithMessage("*UserId*");
    }

    [Fact]
    public void SetAsDefault_SetsIsDefaultTrue()
    {
        var card = PaymentTestDataFactory.CreateSavedCard();

        card.SetAsDefault();

        card.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void ClearDefault_SetsIsDefaultFalse()
    {
        var card = PaymentTestDataFactory.CreateSavedCard();
        card.SetAsDefault();

        card.ClearDefault();

        card.IsDefault.Should().BeFalse();
    }
}
