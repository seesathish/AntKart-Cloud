using AK.Discount.Application.DTOs;
using MediatR;
namespace AK.Discount.Application.Commands.CreateDiscount;
public record CreateDiscountCommand(CreateCouponDto Dto) : IRequest<CouponDto>;
