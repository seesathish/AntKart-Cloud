using System.Net;
using AK.Notification.Core.Channels;
using static AK.Notification.Core.Templates.NotificationDataKeys;
using static AK.Notification.Core.Templates.TemplateEncoding;

namespace AK.Notification.Core.Templates;

// The five customer notification templates — one class per NotificationType (resolved by that type).
// Each composes a subject, an HTML body, and a plain-text body from the loose event data. The text
// is ported from the previous AK.Notification renderer; HTML values are encoded to stay well-formed.
//
// Adding a sixth notification type = add a NotificationType value, add a template class here, and
// register it in AddNotificationCore. Nothing else changes.

internal sealed class OrderCreatedTemplate : INotificationTemplate
{
    public NotificationType NotificationType => NotificationType.OrderCreated;

    public NotificationContent Render(IReadOnlyDictionary<string, string?> data)
    {
        var name = data.GetOrDefault(CustomerName, "Customer");
        var order = data.GetOrDefault(OrderNumber);
        var total = data.GetOrDefault(TotalAmount);

        var subject = $"Order Confirmation — {order}";
        var plain =
            $"Hi {name},\n\nThank you for your order! Your order {order} has been received.\n\n" +
            $"Order Total: ₹{total}\n\nWe will notify you when your order ships.\n\n— The AntKart Team";
        var html =
            $"<p>Hi {Enc(name)},</p>" +
            $"<p>Thank you for your order! Your order <strong>{Enc(order)}</strong> has been received.</p>" +
            $"<p>Order Total: ₹{Enc(total)}</p>" +
            "<p>We will notify you when your order ships.</p><p>— The AntKart Team</p>";
        return new NotificationContent(subject, html, plain);
    }
}

internal sealed class OrderConfirmedTemplate : INotificationTemplate
{
    public NotificationType NotificationType => NotificationType.OrderConfirmed;

    public NotificationContent Render(IReadOnlyDictionary<string, string?> data)
    {
        var name = data.GetOrDefault(CustomerName, "Customer");
        var order = data.GetOrDefault(OrderNumber);
        var total = data.GetOrDefault(TotalAmount);

        var subject = $"Your Order {order} Has Been Confirmed";
        var plain =
            $"Hi {name},\n\nGreat news! Your order {order} has been confirmed and is being processed.\n\n" +
            $"Order Total: ₹{total}\n\nYou will receive another update when your order ships.\n\n— The AntKart Team";
        var html =
            $"<p>Hi {Enc(name)},</p>" +
            $"<p>Great news! Your order <strong>{Enc(order)}</strong> has been confirmed and is being processed.</p>" +
            $"<p>Order Total: ₹{Enc(total)}</p>" +
            "<p>You will receive another update when your order ships.</p><p>— The AntKart Team</p>";
        return new NotificationContent(subject, html, plain);
    }
}

internal sealed class OrderCancelledTemplate : INotificationTemplate
{
    public NotificationType NotificationType => NotificationType.OrderCancelled;

    public NotificationContent Render(IReadOnlyDictionary<string, string?> data)
    {
        var name = data.GetOrDefault(CustomerName, "Customer");
        var order = data.GetOrDefault(OrderNumber);
        var reason = data.GetOrDefault(Reason, "No reason provided");

        var subject = $"Your Order {order} Has Been Cancelled";
        var plain =
            $"Hi {name},\n\nYour order {order} has been cancelled.\n\nReason: {reason}\n\n" +
            "If you have any questions, please contact our support team.\n\n— The AntKart Team";
        var html =
            $"<p>Hi {Enc(name)},</p>" +
            $"<p>Your order <strong>{Enc(order)}</strong> has been cancelled.</p>" +
            $"<p>Reason: {Enc(reason)}</p>" +
            "<p>If you have any questions, please contact our support team.</p><p>— The AntKart Team</p>";
        return new NotificationContent(subject, html, plain);
    }
}

internal sealed class PaymentSucceededTemplate : INotificationTemplate
{
    public NotificationType NotificationType => NotificationType.PaymentSucceeded;

    public NotificationContent Render(IReadOnlyDictionary<string, string?> data)
    {
        var name = data.GetOrDefault(CustomerName, "Customer");
        var order = data.GetOrDefault(OrderNumber);
        var amount = data.GetOrDefault(Amount);
        var paymentId = data.GetOrDefault(PaymentId);

        var subject = $"Payment Confirmed for Order {order}";
        var plain =
            $"Hi {name},\n\nYour payment has been successfully processed.\n\n" +
            $"Order Number: {order}\nAmount Paid: ₹{amount}\nPayment ID: {paymentId}\n\n" +
            "Thank you for shopping with AntKart!\n\n— The AntKart Team";
        var html =
            $"<p>Hi {Enc(name)},</p>" +
            "<p>Your payment has been successfully processed.</p>" +
            $"<p>Order Number: <strong>{Enc(order)}</strong><br/>Amount Paid: ₹{Enc(amount)}<br/>Payment ID: {Enc(paymentId)}</p>" +
            "<p>Thank you for shopping with AntKart!</p><p>— The AntKart Team</p>";
        return new NotificationContent(subject, html, plain);
    }
}

internal sealed class PaymentFailedTemplate : INotificationTemplate
{
    public NotificationType NotificationType => NotificationType.PaymentFailed;

    public NotificationContent Render(IReadOnlyDictionary<string, string?> data)
    {
        var name = data.GetOrDefault(CustomerName, "Customer");
        var order = data.GetOrDefault(OrderNumber);
        var reason = data.GetOrDefault(Reason, "No reason provided");

        var subject = $"Payment Failed for Order {order}";
        var plain =
            $"Hi {name},\n\nUnfortunately, your payment for order {order} could not be processed.\n\n" +
            $"Reason: {reason}\n\nPlease try again or contact our support team for assistance.\n\n— The AntKart Team";
        var html =
            $"<p>Hi {Enc(name)},</p>" +
            $"<p>Unfortunately, your payment for order <strong>{Enc(order)}</strong> could not be processed.</p>" +
            $"<p>Reason: {Enc(reason)}</p>" +
            "<p>Please try again or contact our support team for assistance.</p><p>— The AntKart Team</p>";
        return new NotificationContent(subject, html, plain);
    }
}

internal static class TemplateEncoding
{
    public static string Enc(string value) => WebUtility.HtmlEncode(value);
}
