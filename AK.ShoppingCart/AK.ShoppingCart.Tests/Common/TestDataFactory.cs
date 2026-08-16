using AK.ShoppingCart.Application.DTOs;
using AK.ShoppingCart.Domain.Entities;

namespace AK.ShoppingCart.Tests.Common;

public static class TestDataFactory
{
    public const string DefaultUserId = "user-001";
    public const string DefaultProductId = "prod-001";
    public const string DefaultProductName = "Men's Classic Shirt";
    public const string DefaultSKU = "MEN-SHRT-001";
    public const decimal DefaultPrice = 999.99m;
    public const int DefaultQuantity = 2;

    public static Cart CreateEmptyCart(string? userId = null) =>
        Cart.Create(userId ?? DefaultUserId);

    public static Cart CreateCartWithItem(string? userId = null, string? productId = null)
    {
        var cart = Cart.Create(userId ?? DefaultUserId);
        cart.AddItem(productId ?? DefaultProductId, DefaultProductName, DefaultSKU, DefaultPrice, DefaultQuantity);
        return cart;
    }

    public static Cart CreateCartWithMultipleItems(string? userId = null)
    {
        var cart = Cart.Create(userId ?? DefaultUserId);
        cart.AddItem("prod-001", "Men's Classic Shirt", "MEN-SHRT-001", 999.99m, 2);
        cart.AddItem("prod-002", "Women's Floral Dress", "WOM-DRES-001", 1499.99m, 1);
        cart.AddItem("prod-003", "Kids Cartoon Tee", "KID-TSHI-001", 399.99m, 3);
        return cart;
    }

    public static AddCartItemDto CreateAddCartItemDto(string? productId = null, int quantity = 2) => new(
        productId ?? DefaultProductId,
        DefaultProductName,
        DefaultSKU,
        DefaultPrice,
        quantity);

    public static CartItem CreateCartItem(string? productId = null) =>
        CartItem.Create(productId ?? DefaultProductId, DefaultProductName, DefaultSKU, DefaultPrice, DefaultQuantity);
}
