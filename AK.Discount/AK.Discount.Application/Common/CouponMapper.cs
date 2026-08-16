using AK.Discount.Application.DTOs;
using AK.Discount.Domain.Entities;
namespace AK.Discount.Application.Common;
internal static class CouponMapper
{
    internal static CouponDto ToDto(this Coupon c) => new(
        c.Id, c.ProductId, c.ProductName, c.CouponCode, c.Description,
        c.Amount, c.DiscountType.ToString(), c.ValidFrom, c.ValidTo, c.IsActive, c.MinimumQuantity);
}
