namespace AK.Discount.Application.DTOs;
public record UpdateCouponDto(
    string ProductName,
    string Description,
    decimal Amount,
    string DiscountType,
    DateTime ValidFrom,
    DateTime ValidTo,
    bool IsActive,
    int MinimumQuantity
);
