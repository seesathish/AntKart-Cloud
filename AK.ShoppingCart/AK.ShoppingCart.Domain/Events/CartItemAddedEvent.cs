namespace AK.ShoppingCart.Domain.Events;

public sealed record CartItemAddedEvent(string UserId, string ProductId, int Quantity);
