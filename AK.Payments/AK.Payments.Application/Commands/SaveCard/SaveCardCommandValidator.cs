using FluentValidation;

namespace AK.Payments.Application.Commands.SaveCard;

public sealed class SaveCardCommandValidator : AbstractValidator<SaveCardCommand>
{
    public SaveCardCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RazorpayCustomerId).NotEmpty();
        RuleFor(x => x.RazorpayPaymentId).NotEmpty();
    }
}
