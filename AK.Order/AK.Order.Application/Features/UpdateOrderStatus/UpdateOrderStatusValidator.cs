using AK.Order.Domain.Enums;
using FluentValidation;

namespace AK.Order.Application.Features.UpdateOrderStatus;

public sealed class UpdateOrderStatusValidator : AbstractValidator<UpdateOrderStatusCommand>
{
    public UpdateOrderStatusValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.NewStatus).IsInEnum();
    }
}
