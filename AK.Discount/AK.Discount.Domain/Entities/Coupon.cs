using AK.Discount.Domain.Enums;

namespace AK.Discount.Domain.Entities;

// Coupon entity for AK.Discount — a lighter domain model than the other services.
// No private setters or factory method because AK.Discount uses a simpler CRUD design
// without domain events. EF Core maps this directly via code-first to PostgreSQL.
//
// Each coupon is linked to a single product via ProductId (the 32-char hex MongoDB Id).
// DiscountType determines how Amount is applied at query time in AK.Products:
//   Percentage → Price - (Price × Amount / 100)
//   Fixed      → Price - Amount
public class Coupon
{
    // Integer primary key (auto-incremented by the database — simpler than Guid for this service).
    public int Id { get; set; }

    // The MongoDB product ID this coupon applies to (matches Product.Id in AK.Products).
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;

    // Coupon codes are stored and compared as uppercase to prevent case-sensitivity issues.
    public string CouponCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Amount: meaning depends on DiscountType (percentage value OR fixed currency amount).
    public decimal Amount { get; set; }
    public DiscountType DiscountType { get; set; }

    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }

    // IsActive: allows temporarily disabling a coupon without deleting it.
    // AK.Products checks IsActive before applying the discount to the price.
    public bool IsActive { get; set; } = true;

    // Minimum quantity the customer must order for this discount to apply.
    public int MinimumQuantity { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
