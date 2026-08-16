using AK.Payments.Application.DTOs;
using AK.Payments.Domain.Entities;

namespace AK.Payments.Application.Mapping;

internal static class PaymentMapper
{
    internal static PaymentDto ToDto(Payment p) => new(
        p.Id, p.OrderId, p.UserId, p.Amount, p.Currency,
        p.Status.ToString(), p.Method.ToString(),
        p.RazorpayOrderId, p.RazorpayPaymentId, p.FailureReason, p.CreatedAt);
}

internal static class SavedCardMapper
{
    internal static SavedCardDto ToDto(SavedCard c) => new(
        c.Id, c.UserId, c.RazorpayCustomerId, c.RazorpayTokenId,
        c.CardNetwork, c.Last4, c.CardType, c.CardName, c.IsDefault, c.CreatedAt);
}
