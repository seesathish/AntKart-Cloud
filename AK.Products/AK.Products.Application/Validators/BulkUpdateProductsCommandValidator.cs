using AK.Products.Application.Commands.BulkUpdateProducts;
using FluentValidation;

namespace AK.Products.Application.Validators;

public sealed class BulkUpdateProductsCommandValidator : AbstractValidator<BulkUpdateProductsCommand>
{
    public BulkUpdateProductsCommandValidator()
    {
        RuleFor(x => x.Updates)
            .NotEmpty().WithMessage("At least one update is required.");

        RuleForEach(x => x.Updates)
            .ChildRules(update =>
            {
                update.RuleFor(x => x.Id).NotEmpty().WithMessage("Product ID is required.");
                update.RuleFor(x => x.Data).NotNull().WithMessage("Update data is required.");
            });
    }
}
