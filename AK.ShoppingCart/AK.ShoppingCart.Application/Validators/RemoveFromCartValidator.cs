using AK.ShoppingCart.Application.Commands.RemoveFromCart;
using FluentValidation;

namespace AK.ShoppingCart.Application.Validators;

public sealed class RemoveFromCartValidator : AbstractValidator<RemoveFromCartCommand>
{
    public RemoveFromCartValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ProductId).NotEmpty().MaximumLength(100);
    }
}
