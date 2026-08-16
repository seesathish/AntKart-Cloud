using AK.ShoppingCart.Infrastructure.Persistence;
using FluentAssertions;

namespace AK.ShoppingCart.Tests.Infrastructure;

public sealed class RedisSettingsTests
{
    [Fact]
    public void DefaultConnectionString_ShouldBeLocalhost()
    {
        var settings = new RedisSettings();
        settings.ConnectionString.Should().Be("localhost:6379");
    }

    [Fact]
    public void DefaultInstanceName_ShouldBeAKCart()
    {
        var settings = new RedisSettings();
        settings.InstanceName.Should().Be("AKCart:");
    }

    [Fact]
    public void DefaultCartExpiryDays_ShouldBeThirty()
    {
        var settings = new RedisSettings();
        settings.CartExpiryDays.Should().Be(30);
    }

    [Fact]
    public void Properties_CanBeSetAndRead()
    {
        var settings = new RedisSettings
        {
            ConnectionString = "redis-host:6380",
            InstanceName = "MyApp:",
            CartExpiryDays = 7
        };

        settings.ConnectionString.Should().Be("redis-host:6380");
        settings.InstanceName.Should().Be("MyApp:");
        settings.CartExpiryDays.Should().Be(7);
    }
}
