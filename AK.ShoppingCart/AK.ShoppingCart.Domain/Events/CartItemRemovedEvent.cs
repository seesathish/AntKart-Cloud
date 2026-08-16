namespace AK.ShoppingCart.Domain.Events;

public sealed record CartItemRemovedEvent(string UserId, string ProductId);
