namespace AK.Payments.Application.DTOs;

public sealed record InitiatePaymentResponse(
    Guid PaymentId,
    string RazorpayOrderId,
    string RazorpayKeyId,
    decimal Amount,
    string Currency);
