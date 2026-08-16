using AK.Notification.Core.Channels;
using AK.Notification.Core.Templates;
using FluentAssertions;

namespace AK.Notification.Tests.Core;

public sealed class NotificationTemplateTests
{
    private static Dictionary<string, string?> Data() => new()
    {
        [NotificationDataKeys.CustomerName] = "Alice",
        [NotificationDataKeys.OrderNumber] = "ORD-20260611-A1B2C3D4",
        [NotificationDataKeys.TotalAmount] = "59.98",
        [NotificationDataKeys.Amount] = "59.98",
        [NotificationDataKeys.PaymentId] = "pay_ABC123",
        [NotificationDataKeys.Reason] = "Card declined"
    };

    [Fact]
    public void OrderCreated_ComposesSubjectAndBodies()
    {
        var c = new OrderCreatedTemplate().Render(Data());

        c.Subject.Should().Contain("ORD-20260611-A1B2C3D4");
        c.PlainTextBody.Should().Contain("Alice").And.Contain("59.98");
        c.HtmlBody.Should().Contain("<p>").And.Contain("ORD-20260611-A1B2C3D4");
    }

    [Fact]
    public void OrderConfirmed_ComposesConfirmedSubject()
    {
        var c = new OrderConfirmedTemplate().Render(Data());

        c.Subject.Should().Contain("Confirmed");
        c.PlainTextBody.Should().Contain("ORD-20260611-A1B2C3D4");
        c.HtmlBody.Should().Contain("confirmed");
    }

    [Fact]
    public void OrderCancelled_IncludesReason()
    {
        var c = new OrderCancelledTemplate().Render(Data());

        c.Subject.Should().Contain("Cancelled");
        c.PlainTextBody.Should().Contain("Card declined");
        c.HtmlBody.Should().Contain("Card declined");
    }

    [Fact]
    public void PaymentSucceeded_IncludesAmountAndPaymentId()
    {
        var c = new PaymentSucceededTemplate().Render(Data());

        c.Subject.Should().Contain("Payment Confirmed");
        c.PlainTextBody.Should().Contain("pay_ABC123").And.Contain("59.98");
        c.HtmlBody.Should().Contain("pay_ABC123");
    }

    [Fact]
    public void PaymentFailed_IncludesReason()
    {
        var c = new PaymentFailedTemplate().Render(Data());

        c.Subject.Should().Contain("Payment Failed");
        c.PlainTextBody.Should().Contain("Card declined");
        c.HtmlBody.Should().Contain("Card declined");
    }

    [Fact]
    public void MissingData_FallsBackGracefully_NoThrow()
    {
        // An empty data map must not throw — templates use safe fallbacks.
        var c = new OrderCreatedTemplate().Render(new Dictionary<string, string?>());

        c.Subject.Should().NotBeNullOrWhiteSpace();
        c.PlainTextBody.Should().Contain("Customer");
    }

    [Fact]
    public void HtmlBody_EncodesDynamicValues()
    {
        var data = new Dictionary<string, string?> { [NotificationDataKeys.CustomerName] = "Tom & Jerry" };

        var c = new OrderCreatedTemplate().Render(data);

        c.HtmlBody.Should().Contain("Tom &amp; Jerry");
        c.PlainTextBody.Should().Contain("Tom & Jerry");
    }

    [Fact]
    public void Resolver_ResolvesEveryNotificationType()
    {
        INotificationTemplate[] all =
        [
            new OrderCreatedTemplate(), new OrderConfirmedTemplate(), new OrderCancelledTemplate(),
            new PaymentSucceededTemplate(), new PaymentFailedTemplate()
        ];
        var resolver = new NotificationTemplateResolver(all);

        foreach (var type in Enum.GetValues<NotificationType>())
            resolver.Resolve(type).NotificationType.Should().Be(type);
    }
}
