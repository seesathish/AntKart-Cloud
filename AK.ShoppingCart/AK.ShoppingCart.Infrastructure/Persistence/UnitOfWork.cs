using AK.ShoppingCart.Application.Interfaces;
using AK.ShoppingCart.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Options;

namespace AK.ShoppingCart.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private ICartRepository? _carts;
    private readonly RedisContext _context;
    private readonly IOptions<RedisSettings> _settings;

    public UnitOfWork(RedisContext context, IOptions<RedisSettings> settings)
    {
        _context = context;
        _settings = settings;
    }

    public ICartRepository Carts => _carts ??= new CartRepository(_context, _settings);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);

    public void Dispose() { }
}
