using AK.ShoppingCart.Infrastructure.Persistence;
using AK.ShoppingCart.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace AK.ShoppingCart.Tests.Infrastructure;

public sealed class UnitOfWorkTests
{
    private static UnitOfWork CreateUnitOfWork()
    {
        var db = new Mock<IDatabase>();
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        var context = new RedisContext(multiplexer.Object);
        var settings = Options.Create(new RedisSettings { InstanceName = "test:", CartExpiryDays = 30 });
        return new UnitOfWork(context, settings);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldAlwaysReturnOne()
    {
        var uow = CreateUnitOfWork();
        var result = await uow.SaveChangesAsync();
        result.Should().Be(1);
    }

    [Fact]
    public async Task SaveChangesAsync_CalledMultipleTimes_ShouldAlwaysReturnOne()
    {
        var uow = CreateUnitOfWork();
        var r1 = await uow.SaveChangesAsync();
        var r2 = await uow.SaveChangesAsync();
        r1.Should().Be(1);
        r2.Should().Be(1);
    }

    [Fact]
    public void Carts_PropertyShouldNotBeNull()
    {
        var uow = CreateUnitOfWork();
        uow.Carts.Should().NotBeNull();
    }

    [Fact]
    public void Carts_CalledTwice_ShouldReturnSameInstance()
    {
        var uow = CreateUnitOfWork();
        var repo1 = uow.Carts;
        var repo2 = uow.Carts;
        repo1.Should().BeSameAs(repo2);
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        var uow = CreateUnitOfWork();
        uow.Invoking(u => u.Dispose()).Should().NotThrow();
    }

    [Fact]
    public void Carts_ShouldBeCartRepository()
    {
        var uow = CreateUnitOfWork();
        uow.Carts.Should().BeOfType<CartRepository>();
    }
}
