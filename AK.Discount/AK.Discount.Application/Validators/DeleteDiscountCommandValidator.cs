using AK.Discount.Application.Commands.DeleteDiscount;
using FluentValidation;

namespace AK.Discount.Application.Validators;

public sealed class DeleteDiscountCommandValidator : AbstractValidator<DeleteDiscountCommand>
{
    public DeleteDiscountCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Discount ID must be greater than zero.");
    }
}
