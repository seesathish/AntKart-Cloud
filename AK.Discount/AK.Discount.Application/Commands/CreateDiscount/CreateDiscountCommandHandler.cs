using AK.Discount.Application.Common;
using AK.Discount.Application.DTOs;
using AK.Discount.Application.Interfaces;
using AK.Discount.Domain.Entities;
using AK.Discount.Domain.Enums;
using MediatR;
namespace AK.Discount.Application.Commands.CreateDiscount;

// Creates a new coupon in PostgreSQL via the CouponRepository.
// Coupon codes are normalised to uppercase before saving so lookups are case-insensitive.
// DiscountType is parsed from the string in the DTO (from gRPC proto int → mapped to enum name in the mapper).
public sealed class CreateDiscountCommandHandler(ICouponRepository repo) : IRequestHandler<CreateDiscountCommand, CouponDto>
{
    public async Task<CouponDto> Handle(CreateDiscountCommand request, CancellationToken ct)
    {
        var dto = request.Dto;

        // Guard against duplicate coupon codes — throw 409 Conflict (mapped by ExceptionInterceptor).
        if (await repo.CouponCodeExistsAsync(dto.CouponCode, ct))
            throw new InvalidOperationException($"Coupon code '{dto.CouponCode}' already exists.");

        var coupon = new Coupon
        {
            ProductId = dto.ProductId,
            ProductName = dto.ProductName,
            CouponCode = dto.CouponCode.ToUpperInvariant(), // normalise to uppercase
            Description = dto.Description,
            Amount = dto.Amount,
            // DiscountType arrives as a string from the DTO (e.g. "Percentage") — parse to enum.
            DiscountType = Enum.Parse<DiscountType>(dto.DiscountType, true),
            ValidFrom = dto.ValidFrom,
            ValidTo = dto.ValidTo,
            MinimumQuantity = dto.MinimumQuantity
        };
        var created = await repo.CreateAsync(coupon, ct);
        return created.ToDto();
    }
}
