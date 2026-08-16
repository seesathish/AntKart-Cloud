using AK.ShoppingCart.Domain.Entities;

namespace AK.ShoppingCart.Application.Interfaces;

public interface ICartRepository
{
    Task<Cart?> GetAsync(string userId, CancellationToken ct = default);
    Task SaveAsync(Cart cart, CancellationToken ct = default);
    Task DeleteAsync(string userId, CancellationToken ct = default);
    Task<bool> ExistsAsync(string userId, CancellationToken ct = default);
}
