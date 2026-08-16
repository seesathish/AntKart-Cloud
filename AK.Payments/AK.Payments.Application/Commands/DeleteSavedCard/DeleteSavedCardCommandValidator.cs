using FluentValidation;

namespace AK.Payments.Application.Commands.DeleteSavedCard;

public sealed class DeleteSavedCardCommandValidator : AbstractValidator<DeleteSavedCardCommand>
{
    public DeleteSavedCardCommandValidator()
    {
        RuleFor(x => x.CardId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}
