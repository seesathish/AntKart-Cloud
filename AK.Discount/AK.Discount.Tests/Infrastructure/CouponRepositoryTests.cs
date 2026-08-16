using AK.Discount.Domain.Entities;
using AK.Discount.Domain.Enums;
using AK.Discount.Infrastructure.Persistence;
using AK.Discount.Infrastructure.Persistence.Repositories;
using AK.Discount.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AK.Discount.Tests.Infrastructure;

public sealed class CouponRepositoryTests
{
    private static DbContextOptions<DiscountContext> GetOptions() =>
        new DbContextOptionsBuilder<DiscountContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    [Fact]
    public async Task GetByProductIdAsync_WithActiveMatching_ShouldReturnCoupon()
    {
        var opts = GetOptions();
        var coupon = TestDataFactory.CreateCoupon("MEN-SHIR-001", 1);
        using var writeCtx = new DiscountContext(opts);
        writeCtx.Coupons.Add(coupon);
        await writeCtx.SaveChangesAsync();

        using var readCtx = new DiscountContext(opts);
        var repo = new CouponRepository(readCtx);
        var result = await repo.GetByProductIdAsync("MEN-SHIR-001");

        result.Should().NotBeNull();
        result!.ProductId.Should().Be("MEN-SHIR-001");
    }

    [Fact]
    public async Task GetByProductIdAsync_WithInactiveCoupon_ShouldReturnNull()
    {
        var opts = GetOptions();
        var coupon = TestDataFactory.CreateCoupon("MEN-SHIR-001", 1);
        coupon.IsActive = false;
        using var writeCtx = new DiscountContext(opts);
        writeCtx.Coupons.Add(coupon);
        await writeCtx.SaveChangesAsync();

        using var readCtx = new DiscountContext(opts);
        var repo = new CouponRepository(readCtx);
        var result = await repo.GetByProductIdAsync("MEN-SHIR-001");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByProductIdAsync_WithNonExistentProductId_ShouldReturnNull()
    {
        var opts = GetOptions();
        using var ctx = new DiscountContext(opts);
        var repo = new CouponRepository(ctx);

        var result = await repo.GetByProductIdAsync("NONEXISTENT");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WithExistingId_ShouldReturnCoupon()
    {
        var opts = GetOptions();
        var coupon = TestDataFactory.CreateCoupon("MEN-SHIR-001", 1);
        using var writeCtx = new DiscountContext(opts);
        writeCtx.Coupons.Add(coupon);
        await writeCtx.SaveChangesAsync();

        using var readCtx = new DiscountContext(opts);
        var repo = new CouponRepository(readCtx);
        var result = await repo.GetByIdAsync(1);

        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistentId_ShouldReturnNull()
    {
        var opts = GetOptions();
        using var ctx = new DiscountContext(opts);
        var repo = new CouponRepository(ctx);

        var result = await repo.GetByIdAsync(999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnPagedResults()
    {
        var opts = GetOptions();
        using var writeCtx = new DiscountContext(opts);
        writeCtx.Coupons.AddRange(
            TestDataFactory.CreateCoupon("SKU-001", 1),
            TestDataFactory.CreateCoupon("SKU-002", 2),
            TestDataFactory.CreateCoupon("SKU-003", 3)
        );
        await writeCtx.SaveChangesAsync();

        using var readCtx = new DiscountContext(opts);
        var repo = new CouponRepository(readCtx);
        var result = await repo.GetAllAsync(1, 2);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllAsync_SecondPage_ShouldReturnRemainingItems()
    {
        var opts = GetOptions();
        using var writeCtx = new DiscountContext(opts);
        writeCtx.Coupons.AddRange(
            TestDataFactory.CreateCoupon("SKU-001", 1),
            TestDataFactory.CreateCoupon("SKU-002", 2),
            TestDataFactory.CreateCoupon("SKU-003", 3)
        );
        await writeCtx.SaveChangesAsync();

        using var readCtx = new DiscountContext(opts);
        var repo = new CouponRepository(readCtx);
        var result = await repo.GetAllAsync(2, 2);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetTotalCountAsync_ShouldReturnCorrectCount()
    {
        var opts = GetOptions();
        using var writeCtx = new DiscountContext(opts);
        writeCtx.Coupons.AddRange(
            TestDataFactory.CreateCoupon("SKU-001", 1),
            TestDataFactory.CreateCoupon("SKU-002", 2)
        );
        await writeCtx.SaveChangesAsync();

        using var readCtx = new DiscountContext(opts);
        var repo = new CouponRepository(readCtx);
        var count = await repo.GetTotalCountAsync();

        count.Should().Be(2);
    }

    [Fact]
    public async Task CreateAsync_ShouldPersistCouponAndReturnIt()
    {
        var opts = GetOptions();
        using var ctx = new DiscountContext(opts);
        var repo = new CouponRepository(ctx);
        var coupon = new Coupon
        {
            ProductId = "MEN-SHIR-001",
            ProductName = "Test Shirt",
            CouponCode = "SAVE10",
            Description = "10% off",
            Amount = 10m,
            DiscountType = DiscountType.Percentage,
            ValidFrom = DateTime.UtcNow.AddDays(-1),
            ValidTo = DateTime.UtcNow.AddDays(30),
            IsActive = true,
            MinimumQuantity = 1
        };

        var result = await repo.CreateAsync(coupon);

        result.Should().NotBeNull();
        result.ProductId.Should().Be("MEN-SHIR-001");
        ctx.Coupons.Should().HaveCount(1);
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistChanges()
    {
        var opts = GetOptions();
        var coupon = TestDataFactory.CreateCoupon("MEN-SHIR-001", 1);
        using var writeCtx = new DiscountContext(opts);
        writeCtx.Coupons.Add(coupon);
        await writeCtx.SaveChangesAsync();

        using var updateCtx = new DiscountContext(opts);
        var repo = new CouponRepository(updateCtx);
        var existing = await repo.GetByIdAsync(1);
        existing!.ProductName = "Updated Name";
        await repo.UpdateAsync(existing);

        using var readCtx = new DiscountContext(opts);
        var readRepo = new CouponRepository(readCtx);
        var updated = await readRepo.GetByIdAsync(1);
        updated!.ProductName.Should().Be("Updated Name");
    }

    [Fact]
    public async Task DeleteAsync_WithExistingId_ShouldReturnTrueAndRemoveCoupon()
    {
        var opts = GetOptions();
        var coupon = TestDataFactory.CreateCoupon("MEN-SHIR-001", 1);
        using var writeCtx = new DiscountContext(opts);
        writeCtx.Coupons.Add(coupon);
        await writeCtx.SaveChangesAsync();

        using var deleteCtx = new DiscountContext(opts);
        var repo = new CouponRepository(deleteCtx);
        var result = await repo.DeleteAsync(1);

        result.Should().BeTrue();

        using var readCtx = new DiscountContext(opts);
        var readRepo = new CouponRepository(readCtx);
        var deleted = await readRepo.GetByIdAsync(1);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentId_ShouldReturnFalse()
    {
        var opts = GetOptions();
        using var ctx = new DiscountContext(opts);
        var repo = new CouponRepository(ctx);

        var result = await repo.DeleteAsync(999);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CouponCodeExistsAsync_WithMatchingCode_ShouldReturnTrue()
    {
        var opts = GetOptions();
        var coupon = TestDataFactory.CreateCoupon("MEN-SHIR-001", 1);
        coupon.CouponCode = "SAVE10";
        using var writeCtx = new DiscountContext(opts);
        writeCtx.Coupons.Add(coupon);
        await writeCtx.SaveChangesAsync();

        using var readCtx = new DiscountContext(opts);
        var repo = new CouponRepository(readCtx);
        var result = await repo.CouponCodeExistsAsync("save10");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CouponCodeExistsAsync_WithNonExistentCode_ShouldReturnFalse()
    {
        var opts = GetOptions();
        using var ctx = new DiscountContext(opts);
        var repo = new CouponRepository(ctx);

        var result = await repo.CouponCodeExistsAsync("NONEXISTENT");

        result.Should().BeFalse();
    }
}
