using AK.ShoppingCart.Application.DTOs;
using AK.ShoppingCart.Domain.Entities;

namespace AK.ShoppingCart.Application.Common;

internal static class CartMapper
{
    internal static CartDto ToDto(Cart cart) => new(
        cart.UserId,
        cart.Items.Select(i => new CartItemDto(i.ProductId, i.ProductName, i.SKU, i.Price, i.Quantity, i.ImageUrl, i.Price * i.Quantity)).ToList().AsReadOnly(),
        cart.TotalAmount,
        cart.TotalItems,
        cart.CreatedAt,
        cart.UpdatedAt);
}
