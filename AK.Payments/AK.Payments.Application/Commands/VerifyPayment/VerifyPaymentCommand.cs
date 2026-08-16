using AK.Payments.Application.DTOs;
using MediatR;

namespace AK.Payments.Application.Commands.VerifyPayment;

public sealed record VerifyPaymentCommand(
    Guid PaymentId,
    string RazorpayOrderId,
    string RazorpayPaymentId,
    string RazorpaySignature) : IRequest<PaymentDto>;
