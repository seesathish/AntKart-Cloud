using AK.Discount.Domain.Entities;
using AK.Discount.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AK.Tools.DiscountSeedLoader;

// Upserts a coupon into AKDiscountDb via the Discount service's DiscountContext (no duplicated EF
// code). The upsert key is ProductId — ONE coupon per product:
//   * existing coupon for that ProductId -> updated in place;
//   * none                               -> inserted.
// Re-running the loader therefore converges to exactly one coupon per product — never duplicates.
public sealed class EfCouponUpsertSink : ICouponUpsertSink
{
    private readonly DiscountContext _context;

    public EfCouponUpsertSink(DiscountContext context) => _context = context;

    // Bulk-deletes every coupon in a single server-side DELETE (no entities loaded into memory),
    // returning the number of rows removed. Run before seeding so each run yields the same clean set
    // and any legacy/orphaned rows are cleared.
    public Task<int> ClearAsync(CancellationToken ct = default) =>
        _context.Coupons.ExecuteDeleteAsync(ct);

    public async Task UpsertAsync(Coupon coupon, CancellationToken ct = default)
    {
        var existing = await _context.Coupons.FirstOrDefaultAsync(c => c.ProductId == coupon.ProductId, ct);

        if (existing is null)
        {
            await _context.Coupons.AddAsync(coupon, ct);
        }
        else
        {
            existing.ProductName = coupon.ProductName;
            existing.CouponCode = coupon.CouponCode;
            existing.Description = coupon.Description;
            existing.Amount = coupon.Amount;
            existing.DiscountType = coupon.DiscountType;
            existing.ValidFrom = coupon.ValidFrom;
            existing.ValidTo = coupon.ValidTo;
            existing.IsActive = coupon.IsActive;
            existing.MinimumQuantity = coupon.MinimumQuantity;
            existing.UpdatedAt = coupon.UpdatedAt;
        }

        await _context.SaveChangesAsync(ct);
    }
}
