using AK.Discount.Domain.Entities;
using AK.Discount.Domain.Enums;
using Microsoft.EntityFrameworkCore;
namespace AK.Discount.Infrastructure.Persistence;
public class DiscountContext(DbContextOptions<DiscountContext> options) : DbContext(options)
{
    public DbSet<Coupon> Coupons => Set<Coupon>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Coupon>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.CouponCode).HasMaxLength(50).IsRequired();
            e.HasIndex(c => c.CouponCode).IsUnique();
            e.HasIndex(c => c.ProductId);
            e.Property(c => c.Amount).HasPrecision(18, 2);
            e.Property(c => c.DiscountType).HasConversion<string>();
            e.Property(c => c.ProductName).HasMaxLength(200).IsRequired();
            e.Property(c => c.ProductId).HasMaxLength(100).IsRequired();
        });
    }
}
