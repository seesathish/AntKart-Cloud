using AK.Discount.Domain.Entities;
using AK.Discount.Domain.Enums;

namespace AK.Tools.DiscountSeedLoader;

// Which rule produced a coupon — used only for the run summary.
public enum SelectionRule
{
    KidsCategory,        // Rule B: every Kids product
    MenShirts,           // Rule B: every Men / Shirts product
    DeterministicSpread  // Rule A: ~20% deterministic spread over the remaining products
}

// A chosen coupon paired with the product it came from and the rule that selected it.
public sealed record SelectedCoupon(SeedProduct Product, Coupon Coupon, SelectionRule Rule);

// The PURE selection logic — no I/O — so it is fully unit-testable and deterministic: the same set of
// products always yields the same coupons.
//
// Each product gets AT MOST ONE coupon. Rule B takes precedence over Rule A:
//   RULE B (category-based, predictable test cases):
//       Kids               -> 15% off
//       Men / Shirts       -> 10% off
//   RULE A (deterministic ~20% spread over products NOT caught by B):
//       selected when SkuHash(sku) % 5 == 0 (~1 in 5), with the discount rotated deterministically
//       across a fixed set of variants so a mix of percentage and fixed amounts is produced.
public static class CouponSelector
{
    // Rotated across for Rule A. Index chosen deterministically from the SKU hash.
    private static readonly (decimal Amount, DiscountType Type)[] SpreadVariants =
    [
        (10m, DiscountType.Percentage),
        (20m, DiscountType.Percentage),
        (25m, DiscountType.Percentage),
        (5m,  DiscountType.FlatAmount),
        (10m, DiscountType.FlatAmount),
    ];

    public static IReadOnlyList<SelectedCoupon> Select(IEnumerable<SeedProduct> products, DateTime nowUtc)
    {
        var result = new List<SelectedCoupon>();

        foreach (var p in products)
        {
            SelectionRule? rule = null;
            decimal amount = 0m;
            var type = DiscountType.Percentage;

            // --- RULE B (precedence) ---
            if (string.Equals(p.CategoryName, "Kids", StringComparison.OrdinalIgnoreCase))
            {
                rule = SelectionRule.KidsCategory;
                amount = 15m;
                type = DiscountType.Percentage;
            }
            else if (string.Equals(p.CategoryName, "Men", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(p.SubCategoryName, "Shirts", StringComparison.OrdinalIgnoreCase))
            {
                rule = SelectionRule.MenShirts;
                amount = 10m;
                type = DiscountType.Percentage;
            }
            else
            {
                // --- RULE A (deterministic spread over the remaining products) ---
                var h = SkuHash.Of(p.Sku);
                if (h % 5 == 0)
                {
                    rule = SelectionRule.DeterministicSpread;
                    var variant = SpreadVariants[(h / 5) % (uint)SpreadVariants.Length];
                    amount = variant.Amount;
                    type = variant.Type;
                }
            }

            if (rule is null)
                continue; // no coupon for this product

            result.Add(new SelectedCoupon(p, BuildCoupon(p, amount, type, nowUtc), rule.Value));
        }

        return result;
    }

    // Builds the Coupon for a chosen product. ProductId is the Cosmos `_id`, so AK.Products'
    // GetDiscount(product_id) finds it and computes discountPrice.
    private static Coupon BuildCoupon(SeedProduct p, decimal amount, DiscountType type, DateTime nowUtc) => new()
    {
        ProductId = p.Id,
        ProductName = p.Name,
        CouponCode = $"SAVE-{p.Sku}".ToUpperInvariant(),
        Description = $"{Describe(amount, type, p.Currency)} on {p.Name}",
        Amount = amount,
        DiscountType = type,
        ValidFrom = nowUtc,
        ValidTo = nowUtc.AddYears(1),
        IsActive = true,
        MinimumQuantity = 1,
        CreatedAt = nowUtc,
        UpdatedAt = nowUtc
    };

    public static string Describe(decimal amount, DiscountType type, string currency) =>
        type == DiscountType.Percentage ? $"{amount:0.##}% off" : $"{amount:0.##} {currency} off";
}
