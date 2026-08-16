using AK.Payments.Application.DTOs;
using MediatR;

namespace AK.Payments.Application.Queries.GetPaymentByOrderId;

public sealed record GetPaymentByOrderIdQuery(Guid OrderId) : IRequest<PaymentDto?>;
