using AK.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AK.Payments.API.Extensions;

public static class MigrationExtensions
{
    public static async Task ApplyMigrationsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        await db.Database.MigrateAsync();
    }
}
