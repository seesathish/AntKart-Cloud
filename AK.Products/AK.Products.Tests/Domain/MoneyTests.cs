using AK.Products.Domain.ValueObjects;
using FluentAssertions;

namespace AK.Products.Tests.Domain;

public sealed class MoneyTests
{
    [Fact]
    public void Constructor_WithValidAmount_ShouldCreateMoney()
    {
        var money = new Money(100.50m, "USD");
        money.Amount.Should().Be(100.50m);
        money.Currency.Should().Be("USD");
    }

    [Fact]
    public void Constructor_WithNegativeAmount_ShouldThrowArgumentException()
    {
        var act = () => new Money(-1m);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithEmptyCurrency_ShouldThrowArgumentException()
    {
        var act = () => new Money(100m, "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Add_WithSameCurrency_ShouldReturnSum()
    {
        var result = new Money(100m, "USD").Add(new Money(50m, "USD"));
        result.Amount.Should().Be(150m);
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public void Add_WithDifferentCurrency_ShouldThrowInvalidOperationException()
    {
        var act = () => new Money(100m, "USD").Add(new Money(50m, "EUR"));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Subtract_WithSameCurrency_ShouldReturnDifference()
    {
        var result = new Money(100m, "USD").Subtract(new Money(30m, "USD"));
        result.Amount.Should().Be(70m);
    }

    [Fact]
    public void Equals_WithSameAmountAndCurrency_ShouldBeTrue()
    {
        var m1 = new Money(100m, "USD");
        var m2 = new Money(100m, "USD");
        m1.Equals(m2).Should().BeTrue();
        (m1 == m2).Should().BeTrue();
    }

    [Fact]
    public void Equals_WithDifferentAmount_ShouldBeFalse()
    {
        new Money(100m, "USD").Equals(new Money(200m, "USD")).Should().BeFalse();
    }

    [Fact]
    public void ToString_ShouldFormatCurrencyAndAmount()
    {
        new Money(99.99m, "USD").ToString().Should().Be("USD 99.99");
    }

    [Fact]
    public void Constructor_ShouldRoundToTwoDecimalPlaces()
    {
        new Money(100.999m, "USD").Amount.Should().Be(101.00m);
    }
}
