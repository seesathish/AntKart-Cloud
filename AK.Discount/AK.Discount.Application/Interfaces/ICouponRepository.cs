using AK.Discount.Domain.Entities;
namespace AK.Discount.Application.Interfaces;
public interface ICouponRepository
{
    Task<Coupon?> GetByProductIdAsync(string productId, CancellationToken ct = default);
    Task<Coupon?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<Coupon>> GetAllAsync(int page, int pageSize, CancellationToken ct = default);
    Task<int> GetTotalCountAsync(CancellationToken ct = default);
    Task<Coupon> CreateAsync(Coupon coupon, CancellationToken ct = default);
    Task<Coupon> UpdateAsync(Coupon coupon, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    Task<bool> CouponCodeExistsAsync(string couponCode, CancellationToken ct = default);
}
