using FluentValidation;

namespace AK.Order.Application.Features.CancelOrder;

public sealed class CancelOrderValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}
