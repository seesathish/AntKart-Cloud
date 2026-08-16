using Microsoft.EntityFrameworkCore;

namespace AK.Notification.Core.Persistence;

// Writes notification history records. Abstracted so the dispatcher can be unit-tested against a
// fake/in-memory store with no live database.
public interface INotificationHistoryStore
{
    Task AddAsync(NotificationHistory record, CancellationToken ct = default);
}

internal sealed class EfNotificationHistoryStore(NotificationHistoryDbContext db) : INotificationHistoryStore
{
    public async Task AddAsync(NotificationHistory record, CancellationToken ct = default)
    {
        db.Add(record);
        await db.SaveChangesAsync(ct);
    }
}
