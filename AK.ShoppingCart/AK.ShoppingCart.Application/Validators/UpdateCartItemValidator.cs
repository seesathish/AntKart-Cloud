using AK.ShoppingCart.Application.Commands.UpdateCartItem;
using FluentValidation;

namespace AK.ShoppingCart.Application.Validators;

public sealed class UpdateCartItemValidator : AbstractValidator<UpdateCartItemCommand>
{
    public UpdateCartItemValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0);
    }
}
