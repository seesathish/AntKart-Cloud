using System.Text.Json;
using AK.ShoppingCart.Application.Interfaces;
using AK.ShoppingCart.Domain.Entities;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AK.ShoppingCart.Infrastructure.Persistence.Repositories;

// Redis-backed cart repository using the Snapshot pattern.
//
// Why snapshots instead of storing domain entities directly?
//   - The domain Cart entity contains DomainEvents (not serialisable) and may evolve.
//   - Snapshot records are simple, stable DTOs purpose-built for Redis storage.
//   - On load: JSON snapshot → plain objects → Cart.Restore() → domain entity.
//   - On save: domain entity → snapshot records → JSON → Redis string with TTL.
//
// Redis key format: "{InstanceName}cart:{userId}"  e.g. "AKCart:cart:abc123"
// TTL: CartExpiryDays (default 30 days) — cart automatically expires if user is inactive.
public sealed class CartRepository : ICartRepository
{
    private readonly IDatabase _db;
    private readonly RedisSettings _settings;

    // Internal snapshot DTOs — only used here. They have no domain logic.
    private sealed record CartSnapshot(string UserId, DateTime CreatedAt, DateTime UpdatedAt, List<CartItemSnapshot> Items);
    private sealed record CartItemSnapshot(string ProductId, string ProductName, string SKU, decimal Price, int Quantity, string? ImageUrl);

    // Production constructor: uses RedisContext which wraps the real StackExchange.Redis connection.
    public CartRepository(RedisContext context, IOptions<RedisSettings> settings)
    {
        _db = context.GetDatabase();
        _settings = settings.Value;
    }

    // Test constructor: accepts a mock IDatabase directly, without needing a real Redis connection.
    public CartRepository(IDatabase database, IOptions<RedisSettings> settings)
    {
        _db = database;
        _settings = settings.Value;
    }

    private string GetKey(string userId) => $"{_settings.InstanceName}cart:{userId}";

    public async Task<Cart?> GetAsync(string userId, CancellationToken ct = default)
    {
        var value = await _db.StringGetAsync(GetKey(userId));
        if (value.IsNullOrEmpty) return null;

        var snapshot = JsonSerializer.Deserialize<CartSnapshot>(value!);
        if (snapshot is null) return null;

        // Reconstruct domain CartItem objects from the snapshot data.
        var items = snapshot.Items.Select(i =>
            CartItem.Restore(i.ProductId, i.ProductName, i.SKU, i.Price, i.Quantity, i.ImageUrl));

        // Cart.Restore bypasses the Create factory to preserve original CreatedAt timestamp.
        return Cart.Restore(snapshot.UserId, snapshot.CreatedAt, snapshot.UpdatedAt, items);
    }

    public async Task SaveAsync(Cart cart, CancellationToken ct = default)
    {
        // Project the domain entity to a serialisable snapshot (no domain events, no behaviour).
        var snapshot = new CartSnapshot(
            cart.UserId,
            cart.CreatedAt,
            cart.UpdatedAt,
            cart.Items.Select(i => new CartItemSnapshot(i.ProductId, i.ProductName, i.SKU, i.Price, i.Quantity, i.ImageUrl)).ToList());

        var json = JsonSerializer.Serialize(snapshot);

        // StringSetAsync overwrites the existing key and resets the TTL.
        // This means an active cart never expires as long as the user keeps interacting.
        await _db.StringSetAsync(GetKey(cart.UserId), json, TimeSpan.FromDays(_settings.CartExpiryDays));
    }

    public async Task DeleteAsync(string userId, CancellationToken ct = default)
    {
        await _db.KeyDeleteAsync(GetKey(userId));
    }

    public async Task<bool> ExistsAsync(string userId, CancellationToken ct = default)
    {
        return await _db.KeyExistsAsync(GetKey(userId));
    }
}
