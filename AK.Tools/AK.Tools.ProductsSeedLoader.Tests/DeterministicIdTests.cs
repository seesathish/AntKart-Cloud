using AK.Tools.ProductsSeedLoader;
using FluentAssertions;

namespace AK.Tools.ProductsSeedLoader.Tests;

public sealed class DeterministicIdTests
{
    [Fact]
    public void FromSku_SameSku_AlwaysSameId()
    {
        DeterministicId.FromSku("MEN-SHIR-001")
            .Should().Be(DeterministicId.FromSku("MEN-SHIR-001"));
    }

    [Fact]
    public void FromSku_DifferentSku_DifferentId()
    {
        DeterministicId.FromSku("MEN-SHIR-001")
            .Should().NotBe(DeterministicId.FromSku("MEN-SHIR-002"));
    }

    [Fact]
    public void FromSku_Returns32LowercaseHexChars()
    {
        var id = DeterministicId.FromSku("WOM-DRES-042");
        id.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public void FromSku_IsWhitespaceInsensitive()
    {
        DeterministicId.FromSku("KID-CAPS-010")
            .Should().Be(DeterministicId.FromSku("  KID-CAPS-010  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromSku_NullOrBlank_Throws(string? sku)
    {
        var act = () => DeterministicId.FromSku(sku!);
        act.Should().Throw<ArgumentException>();
    }
}
