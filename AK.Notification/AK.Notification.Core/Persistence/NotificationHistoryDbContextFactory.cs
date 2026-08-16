using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AK.Notification.Core.Persistence;

// Design-time factory used ONLY by `dotnet ef` (e.g. migrations) so the EF tooling can build the
// context from this class library without a running host. The connection string here is a
// design-time placeholder — migrations generate SQL from the model and never connect. At runtime
// the context is configured (secret-less) by AddNotificationCore.
internal sealed class NotificationHistoryDbContextFactory : IDesignTimeDbContextFactory<NotificationHistoryDbContext>
{
    public NotificationHistoryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NotificationHistoryDbContext>()
            .UseNpgsql("Host=localhost;Database=AKNotificationsDb;Username=postgres;Password=postgres")
            .Options;

        return new NotificationHistoryDbContext(options);
    }
}
