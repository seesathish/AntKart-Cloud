using AK.BuildingBlocks.DDD;
using AK.Payments.Domain.Enums;
using AK.Payments.Domain.Events;

namespace AK.Payments.Domain.Entities;

// Payment entity: represents a single payment attempt for an order.
// One order can have at most one active payment record.
//
// Payment lifecycle (status transitions):
//   Pending → Initiated (Razorpay order created, waiting for user to pay)
//          → Succeeded (Razorpay signature verified)
//          → Failed    (signature mismatch or user cancelled)
//
// PCI compliance: we never store raw card numbers. SavedCardToken holds a Razorpay token ID
// (a reference to a tokenised card stored on Razorpay's servers). This keeps us out of PCI scope.
public sealed class Payment : Entity, IAggregateRoot
{
    public Guid OrderId { get; private set; }

    // UserId, CustomerEmail, CustomerName, OrderNumber are denormalised from the order
    // so payment history can be displayed without joining to the Orders database.
    public string UserId { get; private set; } = string.Empty;
    public string CustomerEmail { get; private set; } = string.Empty;
    public string CustomerName { get; private set; } = string.Empty;
    public string OrderNumber { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "INR";
    public PaymentStatus Status { get; private set; }
    public PaymentMethod Method { get; private set; }

    // Razorpay IDs set during the payment flow (null until the respective step completes).
    public string? RazorpayOrderId { get; private set; }    // set after CreateOrder call
    public string? RazorpayPaymentId { get; private set; }  // set after user pays
    public string? RazorpaySignature { get; private set; }  // HMAC-SHA256 signature from Razorpay
    public string? FailureReason { get; private set; }

    // Optional token for paying with a saved card (Razorpay token ID, not raw card data).
    public string? SavedCardToken { get; private set; }

    private Payment() { }

    public static Payment Create(
        Guid orderId,
        string userId,
        string customerEmail,
        string customerName,
        string orderNumber,
        decimal amount,
        PaymentMethod method,
        string? savedCardToken = null)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("OrderId is required.", nameof(orderId));
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(orderNumber))
            throw new ArgumentException("OrderNumber is required.", nameof(orderNumber));
        if (amount <= 0)
            throw new ArgumentException("Amount must be greater than zero.", nameof(amount));

        var payment = new Payment
        {
            OrderId = orderId,
            UserId = userId.Trim(),
            CustomerEmail = customerEmail.Trim(),
            CustomerName = customerName.Trim(),
            OrderNumber = orderNumber.Trim(),
            Amount = amount,
            Method = method,
            Status = PaymentStatus.Pending,
            SavedCardToken = savedCardToken
        };

        payment.AddDomainEvent(new PaymentCreatedEvent(payment.Id, orderId, userId));
        return payment;
    }

    // Called once Razorpay creates an order and returns an order ID.
    // The Razorpay order ID is what the frontend uses to open the payment widget.
    public void AssignRazorpayOrder(string razorpayOrderId)
    {
        if (string.IsNullOrWhiteSpace(razorpayOrderId))
            throw new ArgumentException("RazorpayOrderId is required.", nameof(razorpayOrderId));

        RazorpayOrderId = razorpayOrderId;
        Status = PaymentStatus.Initiated;
        SetUpdatedAt();
    }

    // Called after the frontend posts the Razorpay payment IDs back to VerifyPayment endpoint.
    // The signature is an HMAC-SHA256 of "razorpay_order_id|razorpay_payment_id" using our key secret.
    public void MarkSucceeded(string razorpayPaymentId, string razorpaySignature)
    {
        if (Status == PaymentStatus.Succeeded)
            throw new InvalidOperationException("Payment is already succeeded.");
        if (Status == PaymentStatus.Failed)
            throw new InvalidOperationException("Cannot succeed a failed payment.");

        RazorpayPaymentId = razorpayPaymentId;
        RazorpaySignature = razorpaySignature;
        Status = PaymentStatus.Succeeded;
        SetUpdatedAt();
        AddDomainEvent(new PaymentSucceededEvent(Id, OrderId, razorpayPaymentId));
    }

    public void MarkFailed(string reason)
    {
        if (Status == PaymentStatus.Succeeded)
            throw new InvalidOperationException("Cannot fail a succeeded payment.");

        FailureReason = reason;
        Status = PaymentStatus.Failed;
        SetUpdatedAt();
        AddDomainEvent(new PaymentFailedEvent(Id, OrderId, reason));
    }
}
