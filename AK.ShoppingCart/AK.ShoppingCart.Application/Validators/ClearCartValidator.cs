using AK.ShoppingCart.Application.Commands.ClearCart;
using FluentValidation;

namespace AK.ShoppingCart.Application.Validators;

public sealed class ClearCartValidator : AbstractValidator<ClearCartCommand>
{
    public ClearCartValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(100);
    }
}
