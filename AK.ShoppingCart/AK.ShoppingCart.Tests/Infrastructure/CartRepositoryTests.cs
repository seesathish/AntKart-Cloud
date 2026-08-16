using System.Text.Json;
using AK.ShoppingCart.Infrastructure.Persistence;
using AK.ShoppingCart.Infrastructure.Persistence.Repositories;
using AK.ShoppingCart.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace AK.ShoppingCart.Tests.Infrastructure;

public sealed class CartRepositoryTests
{
    private sealed record CartSnapshotTest(string UserId, DateTime CreatedAt, DateTime UpdatedAt, List<CartItemSnapshotTest> Items);
    private sealed record CartItemSnapshotTest(string ProductId, string ProductName, string SKU, decimal Price, int Quantity, string? ImageUrl);

    private static (CartRepository repo, Mock<IDatabase> db) CreateRepo()
    {
        var db = new Mock<IDatabase>();
        var settings = Options.Create(new RedisSettings { InstanceName = "test:", CartExpiryDays = 30 });
        return (new CartRepository(db.Object, settings), db);
    }

    private static string SerializeCart(string userId, List<CartItemSnapshotTest> items)
    {
        var snapshot = new CartSnapshotTest(userId, DateTime.UtcNow, DateTime.UtcNow, items);
        return JsonSerializer.Serialize(snapshot);
    }

    [Fact]
    public async Task GetAsync_WhenCartExists_ShouldReturnCart()
    {
        var (repo, db) = CreateRepo();
        var items = new List<CartItemSnapshotTest>
        {
            new("prod-001", "Shirt", "MEN-001", 999m, 2, null)
        };
        var json = SerializeCart(TestDataFactory.DefaultUserId, items);
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)json);

        var result = await repo.GetAsync(TestDataFactory.DefaultUserId);

        result.Should().NotBeNull();
        result!.UserId.Should().Be(TestDataFactory.DefaultUserId);
        result.Items.Should().HaveCount(1);
        result.Items[0].ProductId.Should().Be("prod-001");
    }

    [Fact]
    public async Task GetAsync_WhenCartDoesNotExist_ShouldReturnNull()
    {
        var (repo, db) = CreateRepo();
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var result = await repo.GetAsync("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WhenCartHasMultipleItems_ShouldRestoreAllItems()
    {
        var (repo, db) = CreateRepo();
        var items = new List<CartItemSnapshotTest>
        {
            new("prod-001", "Shirt", "MEN-001", 999m, 2, null),
            new("prod-002", "Dress", "WOM-001", 1499m, 1, "https://img.com/dress.jpg"),
            new("prod-003", "Tee", "KID-001", 399m, 3, null)
        };
        var json = SerializeCart(TestDataFactory.DefaultUserId, items);
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)json);

        var result = await repo.GetAsync(TestDataFactory.DefaultUserId);

        result!.Items.Should().HaveCount(3);
        result.TotalItems.Should().Be(6);
    }

    [Fact]
    public async Task SaveAsync_ShouldCallStringSetAsync()
    {
        var (repo, db) = CreateRepo();
        var cart = TestDataFactory.CreateCartWithItem();
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        await repo.SaveAsync(cart);

        db.Verify(d => d.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_ShouldSerializeCartWithExpiry()
    {
        var (repo, db) = CreateRepo();
        var cart = TestDataFactory.CreateCartWithMultipleItems();
        TimeSpan? capturedExpiry = null;
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags>((_, _, expiry, _, _, _) => capturedExpiry = expiry)
            .ReturnsAsync(true);

        await repo.SaveAsync(cart);

        capturedExpiry.Should().Be(TimeSpan.FromDays(30));
    }

    [Fact]
    public async Task DeleteAsync_ShouldCallKeyDeleteAsync()
    {
        var (repo, db) = CreateRepo();
        db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        await repo.DeleteAsync(TestDataFactory.DefaultUserId);

        db.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task ExistsAsync_WhenKeyExists_ShouldReturnTrue()
    {
        var (repo, db) = CreateRepo();
        db.Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var result = await repo.ExistsAsync(TestDataFactory.DefaultUserId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WhenKeyDoesNotExist_ShouldReturnFalse()
    {
        var (repo, db) = CreateRepo();
        db.Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        var result = await repo.ExistsAsync("nonexistent");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_KeyUsesInstanceNamePrefix()
    {
        var (repo, db) = CreateRepo();
        RedisKey capturedKey = default;
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, CommandFlags>((key, _) => capturedKey = key)
            .ReturnsAsync(RedisValue.Null);

        await repo.GetAsync("user-abc");

        capturedKey.ToString().Should().Be("test:cart:user-abc");
    }
}
