namespace AK.ShoppingCart.Application.Interfaces;

public interface IUnitOfWork : IDisposable
{
    ICartRepository Carts { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
