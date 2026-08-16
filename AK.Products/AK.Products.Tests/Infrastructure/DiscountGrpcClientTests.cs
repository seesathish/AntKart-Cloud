using AK.Discount.Grpc;
using AK.Products.Infrastructure.Grpc;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AK.Products.Tests.Infrastructure;

public sealed class DiscountGrpcClientTests
{
    private static Mock<DiscountProtoService.DiscountProtoServiceClient> MockProtoClient() => new();

    private static DiscountGrpcClient Create(DiscountProtoService.DiscountProtoServiceClient proto) =>
        new(proto, NullLogger<DiscountGrpcClient>.Instance);

    [Fact]
    public async Task GetDiscountAsync_WhenServiceUnavailable_ReturnsNull_AndDoesNotThrow()
    {
        var proto = MockProtoClient();
        proto.Setup(c => c.GetDiscountAsync(
                It.IsAny<GetDiscountRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "discount service is down")));

        var client = Create(proto.Object);

        var act = async () => await client.GetDiscountAsync("prod-1");

        var result = await act.Should().NotThrowAsync();
        result.Subject.Should().BeNull();
    }

    [Fact]
    public async Task GetDiscountAsync_WhenDeadlineExceeded_ReturnsNull()
    {
        var proto = MockProtoClient();
        proto.Setup(c => c.GetDiscountAsync(
                It.IsAny<GetDiscountRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.DeadlineExceeded, "timed out")));

        var result = await Create(proto.Object).GetDiscountAsync("prod-1");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDiscountAsync_WhenNotFound_ReturnsNull()
    {
        var proto = MockProtoClient();
        proto.Setup(c => c.GetDiscountAsync(
                It.IsAny<GetDiscountRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.NotFound, "no coupon")));

        var result = await Create(proto.Object).GetDiscountAsync("prod-1");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDiscountAsync_WhenAvailable_ReturnsDiscount()
    {
        var response = new CouponModel { Amount = 10, DiscountType = "Percentage", IsActive = true };
        var call = new AsyncUnaryCall<CouponModel>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

        var proto = MockProtoClient();
        proto.Setup(c => c.GetDiscountAsync(
                It.IsAny<GetDiscountRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(call);

        var result = await Create(proto.Object).GetDiscountAsync("prod-1");

        result.Should().NotBeNull();
        result!.Amount.Should().Be(10);
        result.DiscountType.Should().Be("Percentage");
        result.IsActive.Should().BeTrue();
    }
}
