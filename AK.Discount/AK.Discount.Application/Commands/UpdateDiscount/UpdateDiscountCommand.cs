using AK.Discount.Application.DTOs;
using MediatR;
namespace AK.Discount.Application.Commands.UpdateDiscount;
public record UpdateDiscountCommand(int Id, UpdateCouponDto Dto) : IRequest<CouponDto>;
