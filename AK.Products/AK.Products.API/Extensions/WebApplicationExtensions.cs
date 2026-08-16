using AK.Products.Infrastructure.Seeders;

namespace AK.Products.API.Extensions;

public static class WebApplicationExtensions
{
    public static async Task SeedDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<ProductSeeder>();
        await seeder.SeedAsync();
        app.Logger.LogInformation("Database seeded with 300 sample products.");
    }
}
