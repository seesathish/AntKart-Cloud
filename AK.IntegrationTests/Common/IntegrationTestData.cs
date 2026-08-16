using AK.BuildingBlocks.Messaging.IntegrationEvents;

namespace AK.IntegrationTests.Common;

public static class IntegrationTestData
{
    public static readonly Guid TestOrderId = Guid.NewGuid();
    public static readonly string TestUserId = "user-test-01";
    public static readonly string TestProductId = "507f1f77bcf86cd799439011";
    public static readonly string TestCustomerEmail = "test@example.com";
    public static readonly string TestCustomerName = "Test Customer";
    public static readonly string TestOrderNumber = "ORD-20260101-TEST0001";

    public static OrderCreatedIntegrationEvent CreateOrderEvent(
        Guid? orderId = null,
        string? userId = null,
        int quantity = 5) => new(
            orderId ?? Guid.NewGuid(),
            userId ?? TestUserId,
            TestCustomerEmail,
            TestCustomerName,
            TestOrderNumber,
            new List<OrderItemPayload>
            {
                new(TestProductId, "MEN-SHIR-001", quantity, 29.99m)
            },
            29.99m * quantity);

    public static StockReservedIntegrationEvent CreateStockReservedEvent(Guid orderId, string? userId = null)
        => new(orderId, userId ?? TestUserId);

    public static StockReservationFailedIntegrationEvent CreateStockFailedEvent(
        Guid orderId,
        string reason = "Insufficient stock for: MEN-SHIR-001",
        string? userId = null)
        => new(orderId, userId ?? TestUserId, reason);

    public static OrderConfirmedIntegrationEvent CreateOrderConfirmedEvent(Guid orderId, string? userId = null)
        => new(orderId, userId ?? TestUserId, TestCustomerEmail, TestCustomerName, TestOrderNumber, 149.95m);

    public static OrderCancelledIntegrationEvent CreateOrderCancelledEvent(
        Guid orderId,
        string reason = "Insufficient stock")
        => new(orderId, TestUserId, TestCustomerEmail, TestCustomerName, TestOrderNumber, reason);

    public static PaymentInitiatedIntegrationEvent CreatePaymentInitiatedEvent(
        Guid? paymentId = null,
        Guid? orderId = null,
        string? userId = null,
        decimal amount = 999.00m)
        => new(
            paymentId ?? Guid.NewGuid(),
            orderId ?? Guid.NewGuid(),
            userId ?? TestUserId,
            amount,
            "INR",
            "order_test_" + Guid.NewGuid().ToString("N")[..8]);

    public static PaymentSucceededIntegrationEvent CreatePaymentSucceededEvent(
        Guid? paymentId = null,
        Guid? orderId = null,
        string? userId = null)
        => new(
            paymentId ?? Guid.NewGuid(),
            orderId ?? Guid.NewGuid(),
            userId ?? TestUserId,
            TestCustomerEmail,
            TestCustomerName,
            TestOrderNumber,
            999.00m,
            "pay_test_" + Guid.NewGuid().ToString("N")[..8]);

    public static PaymentFailedIntegrationEvent CreatePaymentFailedEvent(
        Guid? paymentId = null,
        Guid? orderId = null,
        string reason = "Signature verification failed.",
        string? userId = null)
        => new(
            paymentId ?? Guid.NewGuid(),
            orderId ?? Guid.NewGuid(),
            userId ?? TestUserId,
            TestCustomerEmail,
            TestCustomerName,
            TestOrderNumber,
            reason);
}
