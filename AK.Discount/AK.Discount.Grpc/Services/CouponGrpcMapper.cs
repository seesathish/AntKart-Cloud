using AK.Discount.Application.DTOs;
namespace AK.Discount.Grpc.Services;
internal static class CouponGrpcMapper
{
    internal static CouponModel ToGrpc(this CouponDto c) => new()
    {
        Id = c.Id,
        ProductId = c.ProductId,
        ProductName = c.ProductName,
        CouponCode = c.CouponCode,
        Description = c.Description,
        Amount = (double)c.Amount,
        DiscountType = c.DiscountType,
        ValidFrom = c.ValidFrom.ToString("O"),
        ValidTo = c.ValidTo.ToString("O"),
        IsActive = c.IsActive,
        MinimumQuantity = c.MinimumQuantity
    };
}
