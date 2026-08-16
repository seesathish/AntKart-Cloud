using System.Net;
using System.Text;
using AK.Order.Application.Common.Exceptions;
using AK.Order.Application.Common.Interfaces;
using AK.Order.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AK.Order.Tests.Infrastructure;

public class HttpCatalogPriceProviderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(responder(request));
    }

    private static HttpCatalogPriceProvider Provider(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var client = new HttpClient(new StubHandler(responder)) { BaseAddress = new Uri("http://localhost/") };
        return new HttpCatalogPriceProvider(client, NullLogger<HttpCatalogPriceProvider>.Instance);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Active_WithDiscount_MapsFound_AndDiscountIsEffective()
    {
        var provider = Provider(_ => Json("""{"id":"abc","status":"Active","price":29.99,"discountPrice":19.99}"""));

        var result = await provider.GetEffectivePricesAsync(["abc"], CancellationToken.None);

        result["abc"].Status.Should().Be(CatalogPriceStatus.Found);
        result["abc"].EffectivePrice.Should().Be(19.99m); // DiscountPrice wins
    }

    [Fact]
    public async Task Inactive_NoDiscount_MapsInactive_AndBasePriceIsEffective()
    {
        var provider = Provider(_ => Json("""{"id":"abc","status":"Inactive","price":29.99,"discountPrice":null}"""));

        var result = await provider.GetEffectivePricesAsync(["abc"], CancellationToken.None);

        result["abc"].Status.Should().Be(CatalogPriceStatus.Inactive);
        result["abc"].EffectivePrice.Should().Be(29.99m); // falls back to Price
    }

    [Fact]
    public async Task NotFound_MapsNotFound()
    {
        var provider = Provider(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await provider.GetEffectivePricesAsync(["abc"], CancellationToken.None);

        result["abc"].Status.Should().Be(CatalogPriceStatus.NotFound);
    }

    [Fact]
    public async Task NonSuccessStatus_FailsClosed_ThrowsCatalogUnavailable()
    {
        var provider = Provider(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var act = async () => await provider.GetEffectivePricesAsync(["abc"], CancellationToken.None);

        await act.Should().ThrowAsync<CatalogUnavailableException>();
    }

    [Fact]
    public async Task Timeout_FailsClosed_ThrowsCatalogUnavailable()
    {
        var provider = Provider(_ => throw new TaskCanceledException("timed out"));

        var act = async () => await provider.GetEffectivePricesAsync(["abc"], CancellationToken.None);

        await act.Should().ThrowAsync<CatalogUnavailableException>();
    }
}
