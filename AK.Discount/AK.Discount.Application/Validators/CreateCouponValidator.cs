using AK.Discount.Application.Commands.CreateDiscount;
using FluentValidation;
namespace AK.Discount.Application.Validators;
public class CreateDiscountValidator : AbstractValidator<CreateDiscountCommand>
{
    public CreateDiscountValidator()
    {
        RuleFor(x => x.Dto.ProductId).NotEmpty();
        RuleFor(x => x.Dto.ProductName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Dto.CouponCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Dto.Amount).GreaterThan(0);
        RuleFor(x => x.Dto.DiscountType).Must(t => t == "Percentage" || t == "FlatAmount")
            .WithMessage("DiscountType must be 'Percentage' or 'FlatAmount'.");
        RuleFor(x => x.Dto.ValidTo).GreaterThan(x => x.Dto.ValidFrom)
            .WithMessage("ValidTo must be after ValidFrom.");
    }
}
