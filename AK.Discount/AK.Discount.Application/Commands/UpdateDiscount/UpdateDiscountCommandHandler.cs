using AK.Discount.Application.Common;
using AK.Discount.Application.DTOs;
using AK.Discount.Application.Interfaces;
using AK.Discount.Domain.Enums;
using MediatR;
namespace AK.Discount.Application.Commands.UpdateDiscount;
public sealed class UpdateDiscountCommandHandler(ICouponRepository repo) : IRequestHandler<UpdateDiscountCommand, CouponDto>
{
    public async Task<CouponDto> Handle(UpdateDiscountCommand request, CancellationToken ct)
    {
        var coupon = await repo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Discount with id '{request.Id}' not found.");
        var dto = request.Dto;
        coupon.ProductName = dto.ProductName;
        coupon.Description = dto.Description;
        coupon.Amount = dto.Amount;
        coupon.DiscountType = Enum.Parse<DiscountType>(dto.DiscountType, true);
        coupon.ValidFrom = dto.ValidFrom;
        coupon.ValidTo = dto.ValidTo;
        coupon.IsActive = dto.IsActive;
        coupon.MinimumQuantity = dto.MinimumQuantity;
        coupon.UpdatedAt = DateTime.UtcNow;
        var updated = await repo.UpdateAsync(coupon, ct);
        return updated.ToDto();
    }
}
