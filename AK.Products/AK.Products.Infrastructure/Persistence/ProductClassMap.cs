using AK.BuildingBlocks.DDD;
using AK.Products.Domain.Entities;
using MongoDB.Bson.Serialization;

namespace AK.Products.Infrastructure.Persistence;

// MongoDB requires explicit class mappings when domain entities don't have Bson attributes.
// We keep Bson attributes OUT of the domain layer to preserve Clean Architecture —
// the domain must not depend on any infrastructure concern.
// This class is registered once at startup (before MongoDbContext is built).
internal static class ProductClassMap
{
    private static bool _registered;

    // Lock ensures thread safety if multiple threads try to register at startup simultaneously.
    private static readonly object _lock = new();

    public static void Register()
    {
        lock (_lock)
        {
            if (_registered) return;  // Guard: RegisterClassMap throws if called twice for the same type.

            BsonClassMap.RegisterClassMap<StringEntity>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
                // DomainEvents is an in-memory list used for event dispatch — it must NOT be
                // serialised to MongoDB. Unmapped here because the property is declared on StringEntity,
                // not on Product; UnmapProperty requires the declaring type to match the class map.
                cm.UnmapProperty(e => e.DomainEvents);
            });

            BsonClassMap.RegisterClassMap<Product>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
            });

            _registered = true;
        }
    }
}
