using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Application.DTOs;
using AK.Payments.Application.Mapping;
using MediatR;

namespace AK.Payments.Application.Queries.GetPaymentById;

public sealed class GetPaymentByIdQueryHandler(IUnitOfWork uow) : IRequestHandler<GetPaymentByIdQuery, PaymentDto?>
{
    public async Task<PaymentDto?> Handle(GetPaymentByIdQuery request, CancellationToken ct)
    {
        var payment = await uow.Payments.GetByIdAsync(request.Id, ct);
        return payment is null ? null : PaymentMapper.ToDto(payment);
    }
}
