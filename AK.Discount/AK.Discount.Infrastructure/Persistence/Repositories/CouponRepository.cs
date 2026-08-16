using AK.Discount.Application.Interfaces;
using AK.Discount.Domain.Entities;
using Microsoft.EntityFrameworkCore;
namespace AK.Discount.Infrastructure.Persistence.Repositories;
public class CouponRepository(DiscountContext context) : ICouponRepository
{
    public async Task<Coupon?> GetByProductIdAsync(string productId, CancellationToken ct = default)
        => await context.Coupons.FirstOrDefaultAsync(c => c.ProductId == productId && c.IsActive, ct);
    public async Task<Coupon?> GetByIdAsync(int id, CancellationToken ct = default)
        => await context.Coupons.FindAsync([id], ct);
    public async Task<IReadOnlyList<Coupon>> GetAllAsync(int page, int pageSize, CancellationToken ct = default)
        => await context.Coupons.OrderBy(c => c.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
    public async Task<int> GetTotalCountAsync(CancellationToken ct = default)
        => await context.Coupons.CountAsync(ct);
    public async Task<Coupon> CreateAsync(Coupon coupon, CancellationToken ct = default)
    {
        context.Coupons.Add(coupon);
        await context.SaveChangesAsync(ct);
        return coupon;
    }
    public async Task<Coupon> UpdateAsync(Coupon coupon, CancellationToken ct = default)
    {
        context.Coupons.Update(coupon);
        await context.SaveChangesAsync(ct);
        return coupon;
    }
    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var coupon = await context.Coupons.FindAsync([id], ct);
        if (coupon is null) return false;
        context.Coupons.Remove(coupon);
        await context.SaveChangesAsync(ct);
        return true;
    }
    public async Task<bool> CouponCodeExistsAsync(string couponCode, CancellationToken ct = default)
        => await context.Coupons.AnyAsync(c => c.CouponCode == couponCode.ToUpperInvariant(), ct);
}
