using AK.BuildingBlocks.Common;
using AK.Discount.Application.DTOs;
using MediatR;
namespace AK.Discount.Application.Queries.GetAllDiscounts;
public record GetAllDiscountsQuery(int Page = 1, int PageSize = 20) : IRequest<PagedResult<CouponDto>>;
