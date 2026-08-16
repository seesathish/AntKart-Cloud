using AK.Discount.Grpc;
using AK.Products.Application.Interfaces;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AK.Products.Infrastructure.Grpc;

public sealed class DiscountGrpcSettings
{
    public string Address { get; init; } = string.Empty;
}

// gRPC client for AK.Discount — used by product query handlers to enrich product results
// with live discount prices without making a separate REST call.
//
// AK.Discount is an OPTIONAL dependency: the catalogue must always render, with or without
// discount data. This client therefore NEVER throws on failure — it returns null and the caller
// treats that as "no discount". The "discount-grpc" named HttpClient is registered with FAIL-FAST
// resilience (short timeout, no retry, quick circuit-break), so a down Discount service cannot slow
// product queries.
//
// NOTE: when running only AK.Products locally (test-from-code), AK.Discount is usually not running.
// That is EXPECTED and handled by design here — products still render, with no discount price.
internal sealed class DiscountGrpcClient : IDiscountGrpcClient
{
    private readonly DiscountProtoService.DiscountProtoServiceClient _client;
    private readonly ILogger<DiscountGrpcClient> _logger;

    // 0 until the first unavailability warning is logged for THIS client instance (one per scope /
    // request). Guards against per-product log spam when a whole page fails to enrich.
    private int _unavailableWarned;

    public DiscountGrpcClient(
        IHttpClientFactory httpClientFactory,
        IOptions<DiscountGrpcSettings> settings,
        ILogger<DiscountGrpcClient> logger)
        : this(BuildClient(httpClientFactory, settings), logger)
    {
    }

    // Test seam: inject the gRPC client directly so the graceful-degradation behaviour can be unit
    // tested without a live Discount service.
    internal DiscountGrpcClient(
        DiscountProtoService.DiscountProtoServiceClient client,
        ILogger<DiscountGrpcClient> logger)
    {
        _client = client;
        _logger = logger;
    }

    private static DiscountProtoService.DiscountProtoServiceClient BuildClient(
        IHttpClientFactory httpClientFactory, IOptions<DiscountGrpcSettings> settings)
    {
        // Reuse the named HttpClient so connection pooling and the fail-fast resilience pipeline apply.
        var httpClient = httpClientFactory.CreateClient("discount-grpc");
        var channel = GrpcChannel.ForAddress(settings.Value.Address,
            new GrpcChannelOptions { HttpClient = httpClient });
        return new DiscountProtoService.DiscountProtoServiceClient(channel);
    }

    public async Task<DiscountResult?> GetDiscountAsync(string productId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetDiscountAsync(
                new GetDiscountRequest { ProductId = productId },
                cancellationToken: ct);
            return new DiscountResult(response.Amount, response.DiscountType, response.IsActive);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            // No coupon configured for this product — a normal, expected outcome. Not logged.
            return null;
        }
        catch (Exception)
        {
            // Discount service unavailable / timed out / circuit open. Degrade QUIETLY: log a single
            // CONCISE line (message only, NO stack trace) the FIRST time it happens for this request,
            // and drop to Debug thereafter — so enriching a page doesn't flood the log with one
            // warning per product. The product is still returned, just without a discounted price.
            if (Interlocked.Exchange(ref _unavailableWarned, 1) == 0)
                _logger.LogWarning(
                    "Discount enrichment skipped for {ProductId}: discount service unavailable", productId);
            else
                _logger.LogDebug(
                    "Discount enrichment skipped for {ProductId}: discount service unavailable", productId);

            return null;
        }
    }
}
