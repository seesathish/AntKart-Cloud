using AK.Discount.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace AK.Discount.Infrastructure.Extensions;
public static class WebApplicationExtensions
{
    // Applies any pending EF Core migrations on startup so the schema is always current.
    // Data seeding is NOT done here: real, Cosmos-correlated coupons are seeded out-of-band by
    // AK.Tools.DiscountSeedLoader (whose ProductId is the product's Cosmos _id, so AK.Products can
    // resolve discounts) — a deliberate, separate operation, not a boot-time side effect.
    public static async Task MigrateAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        await context.Database.MigrateAsync();
    }
}
