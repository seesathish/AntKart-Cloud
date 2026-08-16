using AK.Notification.Core.Channels;
using AK.Notification.Core.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AK.Notification.Tests.Core;

public sealed class NotificationHistoryStoreTests
{
    private static NotificationHistoryDbContext NewContext() =>
        new(new DbContextOptionsBuilder<NotificationHistoryDbContext>()
            .UseInMemoryDatabase($"hist-{Guid.NewGuid()}").Options);

    [Fact]
    public async Task AddAsync_PersistsRecord()
    {
        await using var db = NewContext();
        var store = new EfNotificationHistoryStore(db);

        await store.AddAsync(NotificationHistory.Record(
            NotificationType.PaymentFailed, "alice@example.com", NotificationChannelType.Email,
            NotificationDeliveryStatus.Failed, correlationId: "ORD-1", errorMessage: "smtp down"));

        var saved = await db.NotificationHistory.SingleAsync();
        saved.NotificationType.Should().Be(NotificationType.PaymentFailed);
        saved.Recipient.Should().Be("alice@example.com");
        saved.ChannelType.Should().Be(NotificationChannelType.Email);
        saved.Status.Should().Be(NotificationDeliveryStatus.Failed);
        saved.CorrelationId.Should().Be("ORD-1");
        saved.ErrorMessage.Should().Be("smtp down");
        saved.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }
}
