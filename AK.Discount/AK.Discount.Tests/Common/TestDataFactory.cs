using AK.Discount.Application.DTOs;
using AK.Discount.Domain.Entities;
using AK.Discount.Domain.Enums;
namespace AK.Discount.Tests.Common;
public static class TestDataFactory
{
    public static Coupon CreateCoupon(string productId = "MEN-SHIR-001", int id = 1) => new()
    {
        Id = id,
        ProductId = productId,
        ProductName = "Test Shirt",
        CouponCode = "TEST-001",
        Description = "Test discount",
        Amount = 10m,
        DiscountType = DiscountType.Percentage,
        ValidFrom = DateTime.UtcNow.AddDays(-1),
        ValidTo = DateTime.UtcNow.AddDays(30),
        IsActive = true,
        MinimumQuantity = 1
    };

    public static CreateCouponDto CreateCouponDto(string productId = "MEN-SHIR-001") => new(
        productId, "Test Shirt", "SAVE10", "10% off test",
        10m, "Percentage", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30));

    public static UpdateCouponDto UpdateCouponDto() => new(
        "Updated Shirt", "Updated description", 15m, "Percentage",
        DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(60), true, 1);
}
