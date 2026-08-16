using AK.Payments.Application.DTOs;
using MediatR;

namespace AK.Payments.Application.Queries.GetPaymentById;

public sealed record GetPaymentByIdQuery(Guid Id) : IRequest<PaymentDto?>;
