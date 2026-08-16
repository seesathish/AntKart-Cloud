using FluentValidation;

namespace AK.Payments.Application.Commands.VerifyPayment;

public sealed class VerifyPaymentCommandValidator : AbstractValidator<VerifyPaymentCommand>
{
    public VerifyPaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.RazorpayOrderId).NotEmpty();
        RuleFor(x => x.RazorpayPaymentId).NotEmpty();
        RuleFor(x => x.RazorpaySignature).NotEmpty();
    }
}
