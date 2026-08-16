using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Application.DTOs;
using AK.Payments.Application.Mapping;
using MediatR;

namespace AK.Payments.Application.Queries.GetUserPayments;

public sealed class GetUserPaymentsQueryHandler(IUnitOfWork uow) : IRequestHandler<GetUserPaymentsQuery, IReadOnlyList<PaymentDto>>
{
    public async Task<IReadOnlyList<PaymentDto>> Handle(GetUserPaymentsQuery request, CancellationToken ct)
    {
        var payments = await uow.Payments.GetByUserIdAsync(request.UserId, ct);
        return payments.Select(PaymentMapper.ToDto).ToList();
    }
}
