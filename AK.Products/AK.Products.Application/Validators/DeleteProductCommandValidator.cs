using AK.Products.Application.Commands.DeleteProduct;
using FluentValidation;

namespace AK.Products.Application.Validators;

public sealed class DeleteProductCommandValidator : AbstractValidator<DeleteProductCommand>
{
    public DeleteProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Product ID is required.");
    }
}
