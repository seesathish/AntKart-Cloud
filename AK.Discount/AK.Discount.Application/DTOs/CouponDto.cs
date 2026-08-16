namespace AK.Discount.Application.DTOs;
public record CouponDto(
    int Id,
    string ProductId,
    string ProductName,
    string CouponCode,
    string Description,
    decimal Amount,
    string DiscountType,
    DateTime ValidFrom,
    DateTime ValidTo,
    bool IsActive,
    int MinimumQuantity
);
