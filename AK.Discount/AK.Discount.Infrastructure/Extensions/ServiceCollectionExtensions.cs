using AK.Discount.Application.Interfaces;
using AK.Discount.Infrastructure.Persistence;
using AK.Discount.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace AK.Discount.Infrastructure.Extensions;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDiscountInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // PostgreSQL connection string comes only from configuration ("DiscountDb"):
        // a localhost default in appsettings for local dev, overridden at runtime by the
        // vaulted ConnectionStrings--DiscountDb secret (Key Vault → ConnectionStrings:DiscountDb).
        var connectionString = configuration.GetConnectionString("DiscountDb");
        services.AddDbContext<DiscountContext>(opts => opts.UseNpgsql(connectionString));
        services.AddScoped<ICouponRepository, CouponRepository>();
        return services;
    }
}
