using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AK.Discount.Infrastructure.Persistence;

// Design-time factory used ONLY by `dotnet ef` (migrations) so the EF tooling can build the
// context without the gRPC host — which would otherwise load Key Vault at startup. The
// connection string here is a local-dev placeholder; `migrations add` generates SQL from the
// model and never connects. At runtime the context is configured by AddDiscountInfrastructure
// from the "DiscountDb" connection string (localhost default, vault-overridden in the cloud).
public sealed class DiscountContextFactory : IDesignTimeDbContextFactory<DiscountContext>
{
    public DiscountContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DiscountContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=AKDiscountDb;Username=postgres;Password=postgres")
            .Options;
        return new DiscountContext(options);
    }
}
