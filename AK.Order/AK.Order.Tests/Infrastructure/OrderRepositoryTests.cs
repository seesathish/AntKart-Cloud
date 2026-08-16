using AK.Order.Domain.Common;
using AK.Order.Domain.Enums;
using AK.Order.Domain.Specifications;
using AK.Order.Infrastructure.Persistence;
using AK.Order.Infrastructure.Persistence.Repositories;
using AK.Order.Tests.Common;
using OrderEntity = AK.Order.Domain.Entities.Order;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AK.Order.Tests.Infrastructure;

public class OrderRepositoryTests : IDisposable
{
    private readonly OrderDbContext _db;
    private readonly OrderRepository _repo;

    public OrderRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new OrderDbContext(options);
        _repo = new OrderRepository(_db);
    }

    [Fact]
    public async Task AddAsync_ThenGetById_ReturnsOrder()
    {
        var order = TestDataFactory.CreateOrder();
        await _repo.AddAsync(order);
        await _db.SaveChangesAsync();

        var fetched = await _repo.GetByIdAsync(order.Id);
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(order.Id);
        fetched.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByOrderNumberAsync_ReturnsCorrectOrder()
    {
        var order = TestDataFactory.CreateOrder();
        await _repo.AddAsync(order);
        await _db.SaveChangesAsync();

        var fetched = await _repo.GetByOrderNumberAsync(order.OrderNumber);
        fetched.Should().NotBeNull();
        fetched!.OrderNumber.Should().Be(order.OrderNumber);
    }

    [Fact]
    public async Task GetByUserIdAsync_ReturnsOrdersForUser()
    {
        var order1 = TestDataFactory.CreateOrder(userId: "user-A");
        var order2 = TestDataFactory.CreateOrder(userId: "user-A");
        var order3 = TestDataFactory.CreateOrder(userId: "user-B");
        await _repo.AddAsync(order1);
        await _repo.AddAsync(order2);
        await _repo.AddAsync(order3);
        await _db.SaveChangesAsync();

        var results = await _repo.GetByUserIdAsync("user-A");
        results.Should().HaveCount(2);
        results.Should().AllSatisfy(o => o.UserId.Should().Be("user-A"));
    }

    [Fact]
    public async Task UpdateAsync_ChangesStatus()
    {
        var order = TestDataFactory.CreateOrder();
        await _repo.AddAsync(order);
        await _db.SaveChangesAsync();

        order.UpdateStatus(OrderStatus.Confirmed);
        await _repo.UpdateAsync(order);
        await _db.SaveChangesAsync();

        var fetched = await _repo.GetByIdAsync(order.Id);
        fetched!.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOrder()
    {
        var order = TestDataFactory.CreateOrder();
        await _repo.AddAsync(order);
        await _db.SaveChangesAsync();

        await _repo.DeleteAsync(order.Id);
        await _db.SaveChangesAsync();

        var fetched = await _repo.GetByIdAsync(order.Id);
        fetched.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_ExistingOrder_ReturnsTrue()
    {
        var order = TestDataFactory.CreateOrder();
        await _repo.AddAsync(order);
        await _db.SaveChangesAsync();

        var exists = await _repo.ExistsAsync(order.Id);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonExistentOrder_ReturnsFalse()
    {
        var exists = await _repo.ExistsAsync(Guid.NewGuid());
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_WithSpecification_FiltersCorrectly()
    {
        var order1 = TestDataFactory.CreateOrder(userId: "user-1");
        var order2 = TestDataFactory.CreateOrder(userId: "user-2");
        await _repo.AddAsync(order1);
        await _repo.AddAsync(order2);
        await _db.SaveChangesAsync();

        var spec = new OrdersPagedSpecification(1, 10, "user-1");
        var results = await _repo.ListAsync(spec);
        results.Should().HaveCount(1);
        results[0].UserId.Should().Be("user-1");
    }

    [Fact]
    public async Task CountAsync_WithSpecification_ReturnsCorrectCount()
    {
        var order1 = TestDataFactory.CreateOrder(userId: "user-X");
        var order2 = TestDataFactory.CreateOrder(userId: "user-X");
        var order3 = TestDataFactory.CreateOrder(userId: "user-Y");
        await _repo.AddAsync(order1);
        await _repo.AddAsync(order2);
        await _repo.AddAsync(order3);
        await _db.SaveChangesAsync();

        var spec = new OrdersPagedSpecification(1, int.MaxValue, "user-X");
        var count = await _repo.CountAsync(spec);
        count.Should().Be(2);
    }

    public void Dispose() => _db.Dispose();
}
