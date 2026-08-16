using AK.Payments.Domain.Entities;
using AK.Payments.Domain.Enums;

namespace AK.Payments.Tests.TestData;

public static class PaymentTestDataFactory
{
    public static readonly Guid OrderId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly string UserId1 = "user1";

    public static Payment CreatePayment(
        Guid? orderId = null,
        string? userId = null,
        string customerEmail = "user1@test.com",
        string customerName = "Test User",
        string orderNumber = "ORD-20260425-TESTTEST",
        decimal amount = 999.00m,
        PaymentMethod method = PaymentMethod.Card)
        => Payment.Create(orderId ?? OrderId1, userId ?? UserId1, customerEmail, customerName, orderNumber, amount, method);

    public static SavedCard CreateSavedCard(string? userId = null)
        => SavedCard.Create(
            userId ?? UserId1,
            "cust_test123",
            "token_test123",
            "Visa",
            "1111",
            "credit",
            "Test User");
}
