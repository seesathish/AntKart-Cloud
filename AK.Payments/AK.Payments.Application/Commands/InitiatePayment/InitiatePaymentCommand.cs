using AK.Payments.Application.DTOs;
using AK.Payments.Domain.Enums;
using MediatR;

namespace AK.Payments.Application.Commands.InitiatePayment;

public sealed record InitiatePaymentCommand(
    Guid OrderId,
    string UserId,
    string CustomerEmail,
    string CustomerName,
    string OrderNumber,
    decimal Amount,
    PaymentMethod Method,
    string? SavedCardToken = null,
    string? CustomerContact = null) : IRequest<InitiatePaymentResponse>;
