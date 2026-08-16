namespace AK.ShoppingCart.Infrastructure.Persistence;

public sealed class RedisSettings
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public string InstanceName { get; set; } = "AKCart:";
    public int CartExpiryDays { get; set; } = 30;
}
