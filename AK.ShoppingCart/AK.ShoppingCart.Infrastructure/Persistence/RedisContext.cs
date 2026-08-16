using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AK.ShoppingCart.Infrastructure.Persistence;

public sealed class RedisContext
{
    private readonly IConnectionMultiplexer _multiplexer;

    public RedisContext(IOptions<RedisSettings> settings)
    {
        _multiplexer = ConnectionMultiplexer.Connect(settings.Value.ConnectionString);
    }

    public RedisContext(IConnectionMultiplexer multiplexer)
    {
        _multiplexer = multiplexer;
    }

    public IDatabase GetDatabase() => _multiplexer.GetDatabase();
}
