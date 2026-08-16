using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AK.Payments.Application.Common.Interfaces;
using Microsoft.Extensions.Options;
using RazorpaySdk = Razorpay.Api.RazorpayClient;

namespace AK.Payments.Infrastructure.External;

// Wraps the Razorpay SDK and REST API calls behind the IRazorpayClient interface.
// The interface abstraction allows unit tests to mock all Razorpay interactions without
// making real network calls.
//
// Two interaction styles are used:
//   - Razorpay C# SDK (synchronous):  order creation, signature verification
//   - Raw HTTP (httpClientFactory):   customers, tokens (saved cards) — not in the SDK
public sealed class RazorpayGatewayClient(IOptions<RazorpaySettings> options, IHttpClientFactory httpClientFactory) : IRazorpayClient
{
    private readonly string _keyId = options.Value.KeyId;
    private readonly string _keySecret = options.Value.KeySecret;
    private const string BaseUrl = "https://api.razorpay.com/v1";

    public Task<RazorpayOrderResponse> CreateOrderAsync(decimal amount, string currency, string receiptId, CancellationToken ct = default)
    {
        var client = new RazorpaySdk(_keyId, _keySecret);
        var opts = new Dictionary<string, object>
        {
            // Razorpay requires amount in the smallest currency unit (paise for INR).
            // ₹100.00 → 10000 paise. The cast to long drops any fractional paise (expected).
            { "amount", (long)(amount * 100) },
            { "currency", currency },
            { "receipt", receiptId }
        };
        var order = client.Order.Create(opts);
        return Task.FromResult(new RazorpayOrderResponse(
            order["id"].ToString()!,
            order["status"].ToString()!,
            long.Parse(order["amount"].ToString()!),
            order["currency"].ToString()!,
            order["receipt"].ToString()!));
    }

    // Signature verification: Razorpay SDK internally computes HMAC-SHA256 of
    // "razorpay_order_id|razorpay_payment_id" using our key secret, then compares
    // to the signature the frontend sent. Throws if they don't match.
    // We catch and return false rather than throwing to keep the caller's logic simple.
    public bool VerifyPaymentSignature(string razorpayOrderId, string razorpayPaymentId, string razorpaySignature)
    {
        var attributes = new Dictionary<string, string>
        {
            { "razorpay_order_id", razorpayOrderId },
            { "razorpay_payment_id", razorpayPaymentId },
            { "razorpay_signature", razorpaySignature }
        };
        try
        {
            Razorpay.Api.Utils.verifyPaymentSignature(attributes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<RazorpayCustomerResponse> CreateCustomerAsync(string name, string email, string contact, CancellationToken ct = default)
    {
        var http = CreateHttpClient();
        var body = JsonSerializer.Serialize(new { name, email, contact });
        var response = await http.PostAsync($"{BaseUrl}/customers",
            new StringContent(body, Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new RazorpayCustomerResponse(
            root.GetProperty("id").GetString()!,
            root.GetProperty("name").GetString()!,
            root.GetProperty("email").GetString()!,
            root.GetProperty("contact").GetString()!);
    }

    public async Task<RazorpayTokenResponse> CreateTokenAsync(string customerId, string paymentId, CancellationToken ct = default)
    {
        var http = CreateHttpClient();
        var response = await http.PostAsync($"{BaseUrl}/customers/{customerId}/tokens",
            new StringContent(JsonSerializer.Serialize(new { payment_id = paymentId }), Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseToken(json, customerId);
    }

    public async Task<IReadOnlyList<RazorpayTokenResponse>> GetTokensAsync(string customerId, CancellationToken ct = default)
    {
        var http = CreateHttpClient();
        var response = await http.GetAsync($"{BaseUrl}/customers/{customerId}/tokens", ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");
        var result = new List<RazorpayTokenResponse>();
        foreach (var item in items.EnumerateArray())
            result.Add(ParseToken(item.GetRawText(), customerId));
        return result;
    }

    public async Task DeleteTokenAsync(string customerId, string tokenId, CancellationToken ct = default)
    {
        var http = CreateHttpClient();
        var response = await http.DeleteAsync($"{BaseUrl}/customers/{customerId}/tokens/{tokenId}", ct);
        response.EnsureSuccessStatusCode();
    }

    // Razorpay REST API uses HTTP Basic Auth: Base64("keyId:keySecret") in the Authorization header.
    // We create a new client per call (from the factory) to avoid mutating shared HttpClient headers.
    private HttpClient CreateHttpClient()
    {
        var http = httpClientFactory.CreateClient("razorpay");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_keyId}:{_keySecret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        return http;
    }

    private static RazorpayTokenResponse ParseToken(string json, string customerId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var card = root.TryGetProperty("card", out var cardEl) ? cardEl : (JsonElement?)null;
        return new RazorpayTokenResponse(
            root.GetProperty("id").GetString()!,
            customerId,
            card?.TryGetProperty("network", out var net) == true ? net.GetString()! : "Unknown",
            card?.TryGetProperty("last4", out var last4) == true ? last4.GetString()! : "****",
            card?.TryGetProperty("card_type", out var type) == true ? type.GetString()! : "credit",
            card?.TryGetProperty("name", out var name) == true ? name.GetString()! : "Cardholder");
    }
}
