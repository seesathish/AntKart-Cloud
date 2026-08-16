using AK.Notification.Core.Channels;
using AK.Notification.Core.Dispatch;
using AK.Notification.Core.Persistence;
using AK.Notification.Core.Templates;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AK.Notification.Tests.Core;

public sealed class NotificationDispatcherTests
{
    private static NotificationHistoryDbContext NewContext() =>
        new(new DbContextOptionsBuilder<NotificationHistoryDbContext>()
            .UseInMemoryDatabase($"notif-{Guid.NewGuid()}").Options);

    private static INotificationTemplateResolver Templates() =>
        new NotificationTemplateResolver(
        [
            new OrderCreatedTemplate(), new OrderConfirmedTemplate(), new OrderCancelledTemplate(),
            new PaymentSucceededTemplate(), new PaymentFailedTemplate()
        ]);

    private static NotificationDispatcher Dispatcher(
        NotificationHistoryDbContext db, params INotificationChannel[] channels) =>
        new(Templates(), channels, new EfNotificationHistoryStore(db), NullLogger<NotificationDispatcher>.Instance);

    private static Mock<INotificationChannel> Channel(NotificationChannelType type, NotificationSendResult result)
    {
        var mock = new Mock<INotificationChannel>();
        mock.SetupGet(c => c.ChannelType).Returns(type);
        mock.Setup(c => c.SendAsync(It.IsAny<NotificationMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    private static NotificationRequest Request(NotificationType type = NotificationType.OrderCreated,
        IReadOnlyCollection<NotificationChannelType>? channels = null) =>
        new(type, "alice@example.com",
            new Dictionary<string, string?>
            {
                [NotificationDataKeys.CustomerName] = "Alice",
                [NotificationDataKeys.OrderNumber] = "ORD-1",
                [NotificationDataKeys.TotalAmount] = "59.98"
            },
            channels);

    [Fact]
    public async Task DispatchAsync_Success_SendsOnChannel_AndPersistsSentRecord()
    {
        await using var db = NewContext();
        var channel = Channel(NotificationChannelType.Email, NotificationSendResult.Sent());

        var result = await Dispatcher(db, channel.Object).DispatchAsync(Request());

        result.AllSucceeded.Should().BeTrue();
        channel.Verify(c => c.SendAsync(
            It.Is<NotificationMessage>(m =>
                m.Recipient == "alice@example.com" &&
                m.NotificationType == NotificationType.OrderCreated &&
                m.CorrelationId == "ORD-1" &&
                m.Subject.Contains("ORD-1")),
            It.IsAny<CancellationToken>()), Times.Once);

        var records = await db.NotificationHistory.ToListAsync();
        records.Should().ContainSingle();
        records[0].Status.Should().Be(NotificationDeliveryStatus.Sent);
        records[0].ChannelType.Should().Be(NotificationChannelType.Email);
        records[0].CorrelationId.Should().Be("ORD-1");
        records[0].ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task DispatchAsync_ChannelFails_PersistsFailedRecord_AndDoesNotThrow()
    {
        await using var db = NewContext();
        var channel = Channel(NotificationChannelType.Email, NotificationSendResult.Failed("smtp down"));

        var act = async () => await Dispatcher(db, channel.Object).DispatchAsync(Request());

        var result = await act.Should().NotThrowAsync();
        result.Subject.AllSucceeded.Should().BeFalse();

        var record = await db.NotificationHistory.SingleAsync();
        record.Status.Should().Be(NotificationDeliveryStatus.Failed);
        record.ErrorMessage.Should().Be("smtp down");
    }

    [Fact]
    public async Task DispatchAsync_ChannelThrows_IsRecordedAsFailed_NoThrow()
    {
        await using var db = NewContext();
        var channel = new Mock<INotificationChannel>();
        channel.SetupGet(c => c.ChannelType).Returns(NotificationChannelType.Email);
        channel.Setup(c => c.SendAsync(It.IsAny<NotificationMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var act = async () => await Dispatcher(db, channel.Object).DispatchAsync(Request());

        await act.Should().NotThrowAsync();
        (await db.NotificationHistory.SingleAsync()).Status.Should().Be(NotificationDeliveryStatus.Failed);
    }

    [Fact]
    public async Task DispatchAsync_RequestedChannelNotRegistered_RecordsFailed_NoThrow()
    {
        await using var db = NewContext();
        // Only Email is registered, but the request targets WhatsApp (not yet shipped).
        var email = Channel(NotificationChannelType.Email, NotificationSendResult.Sent());

        var result = await Dispatcher(db, email.Object)
            .DispatchAsync(Request(channels: [NotificationChannelType.WhatsApp]));

        result.AllSucceeded.Should().BeFalse();
        var record = await db.NotificationHistory.SingleAsync();
        record.ChannelType.Should().Be(NotificationChannelType.WhatsApp);
        record.Status.Should().Be(NotificationDeliveryStatus.Failed);
    }

    [Fact]
    public async Task DispatchAsync_DefaultsToEmail_WhenNoChannelsSpecified()
    {
        await using var db = NewContext();
        var channel = Channel(NotificationChannelType.Email, NotificationSendResult.Sent());

        await Dispatcher(db, channel.Object).DispatchAsync(Request(channels: null));

        (await db.NotificationHistory.SingleAsync()).ChannelType.Should().Be(NotificationChannelType.Email);
    }
}
