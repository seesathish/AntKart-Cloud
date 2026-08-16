using AK.Discount.Application.Commands.CreateDiscount;
using AK.Discount.Application.Commands.DeleteDiscount;
using AK.Discount.Application.Commands.UpdateDiscount;
using AK.Discount.Application.DTOs;
using AK.Discount.Application.Queries.GetAllDiscounts;
using AK.Discount.Application.Queries.GetDiscountByProductId;
using Grpc.Core;
using MediatR;
namespace AK.Discount.Grpc.Services;

// gRPC service implementation for AK.Discount.
// Extends the auto-generated base class from discount.proto — all RPC methods override abstract base methods.
//
// Transport: gRPC over HTTP/2 (faster than REST for internal service calls, strongly typed via Protobuf).
// Called by: AK.Products (via DiscountGrpcClient) when returning product details — fetches the discount
// for each product and computes the discounted price.
//
// Each method delegates to a MediatR command/query to keep the gRPC layer thin (no business logic here).
// Exceptions thrown here are automatically converted to gRPC status codes by Grpc.AspNetCore.
public class DiscountService(ISender sender, ILogger<DiscountService> logger) : DiscountProtoService.DiscountProtoServiceBase
{
    public override async Task<CouponModel> GetDiscount(GetDiscountRequest request, ServerCallContext context)
    {
        logger.LogInformation("GetDiscount called for ProductId: {ProductId}", request.ProductId);
        var coupon = await sender.Send(new GetDiscountByProductIdQuery(request.ProductId), context.CancellationToken);

        // NotFound RpcException: AK.Products' DiscountGrpcClient catches this and returns null,
        // meaning "no discount available for this product" — not a fatal error.
        if (coupon is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"No active discount for product '{request.ProductId}'."));
        return coupon.ToGrpc();
    }

    public override async Task<CouponModel> CreateDiscount(CreateDiscountRequest request, ServerCallContext context)
    {
        var dto = new CreateCouponDto(
            request.Coupon.ProductId, request.Coupon.ProductName, request.Coupon.CouponCode,
            request.Coupon.Description, (decimal)request.Coupon.Amount, request.Coupon.DiscountType,
            DateTime.Parse(request.Coupon.ValidFrom), DateTime.Parse(request.Coupon.ValidTo),
            request.Coupon.MinimumQuantity);
        var result = await sender.Send(new CreateDiscountCommand(dto), context.CancellationToken);
        return result.ToGrpc();
    }

    public override async Task<CouponModel> UpdateDiscount(UpdateDiscountRequest request, ServerCallContext context)
    {
        var dto = new UpdateCouponDto(
            request.Coupon.ProductName, request.Coupon.Description, (decimal)request.Coupon.Amount,
            request.Coupon.DiscountType, DateTime.Parse(request.Coupon.ValidFrom),
            DateTime.Parse(request.Coupon.ValidTo), request.Coupon.IsActive, request.Coupon.MinimumQuantity);
        var result = await sender.Send(new UpdateDiscountCommand(request.Id, dto), context.CancellationToken);
        return result.ToGrpc();
    }

    public override async Task<DeleteDiscountResponse> DeleteDiscount(DeleteDiscountRequest request, ServerCallContext context)
    {
        var success = await sender.Send(new DeleteDiscountCommand(request.Id), context.CancellationToken);
        return new DeleteDiscountResponse { Success = success };
    }

    public override async Task<GetAllDiscountsResponse> GetAllDiscounts(GetAllDiscountsRequest request, ServerCallContext context)
    {
        var result = await sender.Send(new GetAllDiscountsQuery(request.Page < 1 ? 1 : request.Page, request.PageSize < 1 ? 20 : request.PageSize), context.CancellationToken);
        var response = new GetAllDiscountsResponse { TotalCount = result.TotalCount };
        response.Coupons.AddRange(result.Items.Select(c => c.ToGrpc()));
        return response;
    }
}
