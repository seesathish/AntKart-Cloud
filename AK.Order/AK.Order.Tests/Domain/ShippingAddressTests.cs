using AK.Order.Domain.ValueObjects;
using AK.Order.Tests.Common;
using FluentAssertions;

namespace AK.Order.Tests.Domain;

public class ShippingAddressTests
{
    [Fact]
    public void Create_ValidInputs_ReturnsShippingAddress()
    {
        var addr = TestDataFactory.CreateShippingAddress();
        addr.FullName.Should().Be("John Doe");
        addr.AddressLine1.Should().Be("123 Main St");
        addr.AddressLine2.Should().BeNull();
        addr.City.Should().Be("Springfield");
        addr.State.Should().Be("IL");
        addr.PostalCode.Should().Be("62701");
        addr.Country.Should().Be("US");
        addr.Phone.Should().Be("+1-555-0100");
    }

    [Theory]
    [InlineData("", "123 Main St", "City", "State", "12345", "US", "+1-555")]
    [InlineData("John", "", "City", "State", "12345", "US", "+1-555")]
    [InlineData("John", "123 Main", "", "State", "12345", "US", "+1-555")]
    [InlineData("John", "123 Main", "City", "", "12345", "US", "+1-555")]
    [InlineData("John", "123 Main", "City", "State", "", "US", "+1-555")]
    [InlineData("John", "123 Main", "City", "State", "12345", "", "+1-555")]
    [InlineData("John", "123 Main", "City", "State", "12345", "US", "")]
    public void Create_MissingRequiredField_ThrowsArgumentException(
        string fullName, string addr1, string city, string state, string postal, string country, string phone)
    {
        var act = () => ShippingAddress.Create(fullName, addr1, null, city, state, postal, country, phone);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToSingleLine_WithoutAddressLine2_FormatsCorrectly()
    {
        var addr = TestDataFactory.CreateShippingAddress();
        addr.ToSingleLine().Should().Be("123 Main St, Springfield, IL 62701, US");
    }

    [Fact]
    public void ToSingleLine_WithAddressLine2_IncludesIt()
    {
        var addr = TestDataFactory.CreateShippingAddress(addressLine2: "Apt 4B");
        addr.ToSingleLine().Should().Contain("Apt 4B");
    }

    [Fact]
    public void Create_TrimsWhitespace()
    {
        var addr = ShippingAddress.Create("  Jane  ", "  456 Oak Ave  ", null, "  Chicago  ", "  IL  ", "  60601  ", "  US  ", "  +1-555-9999  ");
        addr.FullName.Should().Be("Jane");
        addr.City.Should().Be("Chicago");
    }

    [Fact]
    public void Equals_IdenticalComponents_ReturnsTrue()
    {
        var a = TestDataFactory.CreateShippingAddress();
        var b = TestDataFactory.CreateShippingAddress();
        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentCity_ReturnsFalse()
    {
        var a = TestDataFactory.CreateShippingAddress();
        var b = TestDataFactory.CreateShippingAddress(city: "Chicago");
        a.Should().NotBe(b);
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void Equals_NullAddressLine2VsValue_ReturnsFalse()
    {
        var a = TestDataFactory.CreateShippingAddress();
        var b = TestDataFactory.CreateShippingAddress(addressLine2: "Apt 4B");
        a.Should().NotBe(b);
    }
}
