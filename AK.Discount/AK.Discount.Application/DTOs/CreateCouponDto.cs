namespace AK.Discount.Application.DTOs;
public record CreateCouponDto(
    string ProductId,
    string ProductName,
    string CouponCode,
    string Description,
    decimal Amount,
    string DiscountType,
    DateTime ValidFrom,
    DateTime ValidTo,
    int MinimumQuantity = 1
);
