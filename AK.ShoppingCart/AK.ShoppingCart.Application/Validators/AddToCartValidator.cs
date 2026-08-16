using AK.ShoppingCart.Application.Commands.AddToCart;
using FluentValidation;

namespace AK.ShoppingCart.Application.Validators;

public sealed class AddToCartValidator : AbstractValidator<AddToCartCommand>
{
    public AddToCartValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Item).NotNull();
        RuleFor(x => x.Item.ProductId).NotEmpty();
        RuleFor(x => x.Item.ProductName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Item.SKU).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Item.Price).GreaterThan(0);
        RuleFor(x => x.Item.Quantity).GreaterThan(0);
    }
}
