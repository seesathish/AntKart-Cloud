using AK.Discount.Application.DTOs;
using MediatR;
namespace AK.Discount.Application.Queries.GetDiscountByProductId;
public record GetDiscountByProductIdQuery(string ProductId) : IRequest<CouponDto?>;
