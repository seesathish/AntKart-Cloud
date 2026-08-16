using AK.Order.Infrastructure.Persistence;
using AK.Order.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AK.Order.Tests.Infrastructure;

public class UnitOfWorkTests : IDisposable
{
    private readonly OrderDbContext _db;
    private readonly UnitOfWork _uow;

    public UnitOfWorkTests()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new OrderDbContext(options);
        _uow = new UnitOfWork(_db);
    }

    [Fact]
    public void Orders_ReturnsRepository()
    {
        _uow.Orders.Should().NotBeNull();
    }

    [Fact]
    public void Orders_ReturnsSameInstance()
    {
        var repo1 = _uow.Orders;
        var repo2 = _uow.Orders;
        repo1.Should().BeSameAs(repo2);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsData()
    {
        var order = TestDataFactory.CreateOrder();
        await _uow.Orders.AddAsync(order);
        await _uow.SaveChangesAsync();

        var fetched = await _uow.Orders.GetByIdAsync(order.Id);
        fetched.Should().NotBeNull();
    }

    public void Dispose() => _uow.Dispose();
}
