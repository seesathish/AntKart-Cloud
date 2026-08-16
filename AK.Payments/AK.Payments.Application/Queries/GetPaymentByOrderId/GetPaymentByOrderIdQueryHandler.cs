using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Application.DTOs;
using AK.Payments.Application.Mapping;
using MediatR;

namespace AK.Payments.Application.Queries.GetPaymentByOrderId;

public sealed class GetPaymentByOrderIdQueryHandler(IUnitOfWork uow) : IRequestHandler<GetPaymentByOrderIdQuery, PaymentDto?>
{
    public async Task<PaymentDto?> Handle(GetPaymentByOrderIdQuery request, CancellationToken ct)
    {
        var payment = await uow.Payments.GetByOrderIdAsync(request.OrderId, ct);
        return payment is null ? null : PaymentMapper.ToDto(payment);
    }
}
