using AK.Discount.Domain.Entities;

namespace AK.Tools.DiscountSeedLoader;

// Where the loader writes each coupon. Abstracted so the orchestration can be unit-tested with a
// fake, no live database required.
public interface ICouponUpsertSink
{
    // Deletes ALL existing coupons (so a run starts from a clean slate) and returns the row count
    // removed. Called before seeding to make the tool authoritative and remove any legacy/orphaned rows.
    Task<int> ClearAsync(CancellationToken ct = default);

    Task UpsertAsync(Coupon coupon, CancellationToken ct = default);
}
