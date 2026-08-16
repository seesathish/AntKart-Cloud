using FluentValidation;

namespace AK.Order.Application.Features.CreateOrder;

public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(100);

        RuleFor(x => x.Order).NotNull();
        RuleFor(x => x.Order.Items).NotEmpty().WithMessage("Order must contain at least one item.");
        RuleForEach(x => x.Order.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.ProductName).NotEmpty().MaximumLength(200);
            item.RuleFor(i => i.SKU).NotEmpty().MaximumLength(50);
            item.RuleFor(i => i.Price).GreaterThan(0);
            item.RuleFor(i => i.Quantity).GreaterThan(0);
        });

        RuleFor(x => x.Order.ShippingAddress).NotNull();
        RuleFor(x => x.Order.ShippingAddress.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Order.ShippingAddress.AddressLine1).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Order.ShippingAddress.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Order.ShippingAddress.State).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Order.ShippingAddress.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Order.ShippingAddress.Country).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Order.ShippingAddress.Phone).NotEmpty().MaximumLength(30);
    }
}
