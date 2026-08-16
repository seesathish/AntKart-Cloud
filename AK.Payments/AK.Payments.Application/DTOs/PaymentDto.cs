namespace AK.Payments.Application.DTOs;

public sealed record PaymentDto(
    Guid Id,
    Guid OrderId,
    string UserId,
    decimal Amount,
    string Currency,
    string Status,
    string Method,
    string? RazorpayOrderId,
    string? RazorpayPaymentId,
    string? FailureReason,
    DateTimeOffset CreatedAt);
