namespace AK.Payments.Application.DTOs;

public sealed record SavedCardDto(
    Guid Id,
    string UserId,
    string RazorpayCustomerId,
    string RazorpayTokenId,
    string CardNetwork,
    string Last4,
    string CardType,
    string CardName,
    bool IsDefault,
    DateTimeOffset CreatedAt);
