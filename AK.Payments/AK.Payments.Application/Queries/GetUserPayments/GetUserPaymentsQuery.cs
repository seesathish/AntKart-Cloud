using AK.Payments.Application.DTOs;
using MediatR;

namespace AK.Payments.Application.Queries.GetUserPayments;

public sealed record GetUserPaymentsQuery(string UserId) : IRequest<IReadOnlyList<PaymentDto>>;
