using AK.BuildingBlocks.Email;
using AK.Notification.Core.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AK.Notification.Tests.Core;

public sealed class EmailNotificationChannelTests
{
    private static NotificationMessage Message() => new(
        Recipient: "alice@example.com",
        Subject: "Order Confirmation — ORD-1",
        HtmlBody: "<p>hi</p>",
        PlainTextBody: "hi",
        ChannelType: NotificationChannelType.Email,
        NotificationType: NotificationType.OrderCreated,
        CorrelationId: "ORD-1");

    [Fact]
    public void ChannelType_IsEmail()
    {
        var channel = new EmailNotificationChannel(Mock.Of<IEmailSender>(), NullLogger<EmailNotificationChannel>.Instance);
        channel.ChannelType.Should().Be(NotificationChannelType.Email);
    }

    [Fact]
    public async Task SendAsync_DelegatesToEmailSender_AndReturnsSent()
    {
        var sender = new Mock<IEmailSender>();
        var channel = new EmailNotificationChannel(sender.Object, NullLogger<EmailNotificationChannel>.Instance);

        var result = await channel.SendAsync(Message());

        result.Success.Should().BeTrue();
        sender.Verify(s => s.SendAsync(
            "alice@example.com", "Order Confirmation — ORD-1", "<p>hi</p>", "hi", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_WhenEmailSenderThrows_ReturnsFailed_DoesNotThrow()
    {
        var sender = new Mock<IEmailSender>();
        sender.Setup(s => s.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ACS unavailable"));
        var channel = new EmailNotificationChannel(sender.Object, NullLogger<EmailNotificationChannel>.Instance);

        var act = async () => await channel.SendAsync(Message());

        var result = await act.Should().NotThrowAsync();
        result.Subject.Success.Should().BeFalse();
        result.Subject.Error.Should().Contain("ACS unavailable");
    }
}
