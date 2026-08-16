using MediatR;
namespace AK.Discount.Application.Commands.DeleteDiscount;
public record DeleteDiscountCommand(int Id) : IRequest<bool>;
