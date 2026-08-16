using AK.Payments.Application.DTOs;
using MediatR;

namespace AK.Payments.Application.Commands.SaveCard;

public sealed record SaveCardCommand(
    string UserId,
    string RazorpayCustomerId,
    string RazorpayPaymentId,
    string CustomerName,
    string CustomerEmail,
    string CustomerContact) : IRequest<SavedCardDto>;
