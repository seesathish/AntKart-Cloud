using AK.Products.Application.DTOs;
using AK.Products.Domain.Entities;

namespace AK.Products.Tests.Common;

public static class TestDataFactory
{
    public static Product CreateProduct(
        string? sku = null,
        string categoryName = "Men",
        string? subCategoryName = "Shirts",
        string name = "Men's Classic Shirt",
        string brand = "ArrowMen",
        decimal price = 999.99m,
        int stock = 50) =>
        Product.Create(name, $"A premium {name.ToLower()}", sku ?? "MEN-SHRT-001",
            brand, categoryName, subCategoryName, price, "USD", stock,
            ["S", "M", "L", "XL"], ["White", "Blue"], "Cotton");

    public static Product CreateMenProduct(string? sku = null) =>
        Product.Create("Men's Classic Shirt", "A premium men's shirt", sku ?? "MEN-SHRT-001",
            "ArrowMen", "Men", "Shirts", 999.99m, "USD", 50,
            ["S", "M", "L", "XL"], ["White", "Blue"], "Cotton");

    public static Product CreateWomenProduct(string? sku = null) =>
        Product.Create("Women's Floral Dress", "A beautiful floral dress", sku ?? "WOM-DRES-001",
            "Biba", "Women", "Dresses", 1499.99m, "USD", 30,
            ["XS", "S", "M"], ["Red", "Pink"], "Chiffon");

    public static Product CreateKidsProduct(string? sku = null) =>
        Product.Create("Kids Cartoon Tee", "Fun cartoon t-shirt for kids", sku ?? "KID-TSHI-001",
            "H&M Kids", "Kids", "T-Shirts", 399.99m, "USD", 100,
            ["3-4Y", "4-5Y", "5-6Y"], ["Blue", "Yellow"], "Soft Cotton");

    public static CreateProductDto CreateProductDto(string? sku = null) => new(
        "Test Shirt", "A test shirt description", sku ?? "TEST-001",
        "TestBrand", "Men", "Shirts", 599.99m, "USD", 25,
        ["S", "M", "L"], ["White"], "Cotton");

    public static UpdateProductDto UpdateProductDto() => new(
        "Updated Shirt", "Updated description", "UpdatedBrand", 699.99m, 30, "Linen");
}
