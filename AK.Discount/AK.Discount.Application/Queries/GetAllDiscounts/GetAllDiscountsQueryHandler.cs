using AK.BuildingBlocks.Common;
using AK.Discount.Application.Common;
using AK.Discount.Application.DTOs;
using AK.Discount.Application.Interfaces;
using MediatR;
namespace AK.Discount.Application.Queries.GetAllDiscounts;
public sealed class GetAllDiscountsQueryHandler(ICouponRepository repo) : IRequestHandler<GetAllDiscountsQuery, PagedResult<CouponDto>>
{
    public async Task<PagedResult<CouponDto>> Handle(GetAllDiscountsQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var items = await repo.GetAllAsync(page, pageSize, ct);
        var total = await repo.GetTotalCountAsync(ct);
        return new PagedResult<CouponDto>(items.Select(c => c.ToDto()).ToList(), total, page, pageSize);
    }
}
