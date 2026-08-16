using AK.Discount.Application.Common;
using AK.Discount.Application.DTOs;
using AK.Discount.Application.Interfaces;
using MediatR;
namespace AK.Discount.Application.Queries.GetDiscountByProductId;
public sealed class GetDiscountByProductIdQueryHandler(ICouponRepository repo) : IRequestHandler<GetDiscountByProductIdQuery, CouponDto?>
{
    public async Task<CouponDto?> Handle(GetDiscountByProductIdQuery request, CancellationToken ct)
    {
        var coupon = await repo.GetByProductIdAsync(request.ProductId, ct);
        return coupon?.ToDto();
    }
}
