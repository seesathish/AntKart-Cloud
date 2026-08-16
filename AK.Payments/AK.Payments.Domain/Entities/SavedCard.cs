using AK.BuildingBlocks.DDD;

namespace AK.Payments.Domain.Entities;

public sealed class SavedCard : Entity
{
    public string UserId { get; private set; } = string.Empty;
    public string RazorpayCustomerId { get; private set; } = string.Empty;
    public string RazorpayTokenId { get; private set; } = string.Empty;
    public string CardNetwork { get; private set; } = string.Empty;
    public string Last4 { get; private set; } = string.Empty;
    public string CardType { get; private set; } = string.Empty;
    public string CardName { get; private set; } = string.Empty;
    public bool IsDefault { get; private set; }

    private SavedCard() { }

    public static SavedCard Create(
        string userId,
        string razorpayCustomerId,
        string razorpayTokenId,
        string cardNetwork,
        string last4,
        string cardType,
        string cardName)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(razorpayTokenId))
            throw new ArgumentException("RazorpayTokenId is required.", nameof(razorpayTokenId));
        if (string.IsNullOrWhiteSpace(razorpayCustomerId))
            throw new ArgumentException("RazorpayCustomerId is required.", nameof(razorpayCustomerId));

        return new SavedCard
        {
            UserId = userId.Trim(),
            RazorpayCustomerId = razorpayCustomerId.Trim(),
            RazorpayTokenId = razorpayTokenId.Trim(),
            CardNetwork = cardNetwork.Trim(),
            Last4 = last4.Trim(),
            CardType = cardType.Trim(),
            CardName = cardName.Trim()
        };
    }

    public void SetAsDefault()
    {
        IsDefault = true;
        SetUpdatedAt();
    }

    public void ClearDefault()
    {
        IsDefault = false;
        SetUpdatedAt();
    }
}
