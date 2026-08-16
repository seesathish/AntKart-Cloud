using AK.Products.Domain.Entities;
using AK.Products.Infrastructure.Persistence;
using AK.Products.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;

namespace AK.Products.Tests.Infrastructure;

public sealed class UnitOfWorkTests
{
    private static UnitOfWork CreateUnitOfWork()
    {
        var collectionMock = new Mock<IMongoCollection<Product>>();
        var dbMock = new Mock<IMongoDatabase>();
        dbMock.Setup(d => d.GetCollection<Product>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(collectionMock.Object);
        var context = new MongoDbContext(dbMock.Object);
        var settings = Options.Create(new MongoDbSettings { ProductsCollection = "products" });
        return new UnitOfWork(context, settings, TestResilience.CosmosPipelines());
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldAlwaysReturnOne()
    {
        var uow = CreateUnitOfWork();
        var result = await uow.SaveChangesAsync();
        result.Should().Be(1);
    }

    [Fact]
    public async Task BeginTransactionAsync_ShouldCompleteWithoutError()
    {
        var uow = CreateUnitOfWork();
        await uow.Invoking(u => u.BeginTransactionAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task CommitTransactionAsync_ShouldCompleteWithoutError()
    {
        var uow = CreateUnitOfWork();
        await uow.Invoking(u => u.CommitTransactionAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task RollbackTransactionAsync_ShouldCompleteWithoutError()
    {
        var uow = CreateUnitOfWork();
        await uow.Invoking(u => u.RollbackTransactionAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        var uow = CreateUnitOfWork();
        uow.Invoking(u => u.Dispose()).Should().NotThrow();
    }

    [Fact]
    public void Products_PropertyShouldNotBeNull()
    {
        var uow = CreateUnitOfWork();
        uow.Products.Should().NotBeNull();
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
}
