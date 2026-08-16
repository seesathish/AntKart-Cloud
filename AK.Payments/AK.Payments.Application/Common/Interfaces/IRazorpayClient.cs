namespace AK.Payments.Application.Common.Interfaces;

public interface IRazorpayClient
{
    Task<RazorpayOrderResponse> CreateOrderAsync(decimal amount, string currency, string receiptId, CancellationToken ct = default);
    bool VerifyPaymentSignature(string razorpayOrderId, string razorpayPaymentId, string razorpaySignature);
    Task<RazorpayCustomerResponse> CreateCustomerAsync(string name, string email, string contact, CancellationToken ct = default);
    Task<RazorpayTokenResponse> CreateTokenAsync(string customerId, string paymentId, CancellationToken ct = default);
    Task<IReadOnlyList<RazorpayTokenResponse>> GetTokensAsync(string customerId, CancellationToken ct = default);
    Task DeleteTokenAsync(string customerId, string tokenId, CancellationToken ct = default);
}

public sealed record RazorpayOrderResponse(string Id, string Status, long Amount, string Currency, string Receipt);
public sealed record RazorpayCustomerResponse(string Id, string Name, string Email, string Contact);
public sealed record RazorpayTokenResponse(string Id, string CustomerId, string CardNetwork, string Last4, string CardType, string CardName);
