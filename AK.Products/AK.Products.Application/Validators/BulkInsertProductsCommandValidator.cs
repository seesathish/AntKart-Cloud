using AK.Products.Application.Commands.BulkInsertProducts;
using FluentValidation;

namespace AK.Products.Application.Validators;

public sealed class BulkInsertProductsCommandValidator : AbstractValidator<BulkInsertProductsCommand>
{
    public BulkInsertProductsCommandValidator()
    {
        RuleFor(x => x.Products)
            .NotEmpty().WithMessage("At least one product is required.");

        RuleForEach(x => x.Products).SetValidator(new CreateProductValidator());
    }
}
