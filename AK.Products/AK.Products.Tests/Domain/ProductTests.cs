using AK.Products.Domain.Entities;
using AK.Products.Domain.Enums;
using AK.Products.Domain.Events;
using AK.Products.Tests.Common;
using FluentAssertions;

namespace AK.Products.Tests.Domain;

public sealed class ProductTests
{
    [Fact]
    public void Create_WithValidData_ShouldCreateProduct()
    {
        var product = TestDataFactory.CreateMenProduct();
        product.Should().NotBeNull();
        product.Name.Should().Be("Men's Classic Shirt");
        product.SKU.Should().Be("MEN-SHRT-001");
        product.CategoryName.Should().Be("Men");
        product.SubCategoryName.Should().Be("Shirts");
        product.Status.Should().Be(ProductStatus.Active);
        product.Price.Should().Be(999.99m);
        product.StockQuantity.Should().Be(50);
    }

    [Fact]
    public void Create_WithZeroStock_ShouldSetOutOfStockStatus()
    {
        var product = Product.Create("Test", "Desc", "SKU-001", "Brand",
            "Men", "Shirts", 100m, "USD", 0, ["M"], ["White"], null);
        product.Status.Should().Be(ProductStatus.OutOfStock);
    }

    [Fact]
    public void Create_WithEmptyName_ShouldThrowException()
    {
        var act = () => Product.Create("", "Desc", "SKU-001", "Brand",
            "Men", "Shirts", 100m, "USD", 10, ["M"], ["White"], null);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithEmptyCategoryName_ShouldThrowException()
    {
        var act = () => Product.Create("Name", "Desc", "SKU-001", "Brand",
            "", "Shirts", 100m, "USD", 10, ["M"], ["White"], null);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_ShouldRaiseDomainEvent()
    {
        var product = TestDataFactory.CreateMenProduct();
        product.DomainEvents.Should().HaveCount(1);
        product.DomainEvents.First().Should().BeOfType<ProductCreatedEvent>();
    }

    [Fact]
    public void Update_ShouldUpdateProductDetails()
    {
        var product = TestDataFactory.CreateMenProduct();
        product.Update("Updated Name", "Updated Desc", "NewBrand", 1299.99m, 75, "Linen");
        product.Name.Should().Be("Updated Name");
        product.Brand.Should().Be("NewBrand");
        product.Price.Should().Be(1299.99m);
        product.StockQuantity.Should().Be(75);
        product.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void SetDiscount_WithValidPrice_ShouldSetDiscountPrice()
    {
        var product = TestDataFactory.CreateMenProduct();
        product.SetDiscount(799.99m);
        product.DiscountPrice.Should().Be(799.99m);
    }

    [Fact]
    public void SetDiscount_WithPriceGreaterThanOrEqualOriginal_ShouldThrowException()
    {
        var product = TestDataFactory.CreateMenProduct();
        var act = () => product.SetDiscount(999.99m);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Discount price must be less than original price");
    }

    [Fact]
    public void RemoveDiscount_ShouldClearDiscountPrice()
    {
        var product = TestDataFactory.CreateMenProduct();
        product.SetDiscount(799.99m);
        product.RemoveDiscount();
        product.DiscountPrice.Should().BeNull();
    }

    [Fact]
    public void SetFeatured_ShouldMarkProductAsFeatured()
    {
        var product = TestDataFactory.CreateMenProduct();
        product.SetFeatured(true);
        product.IsFeatured.Should().BeTrue();
    }

    [Fact]
    public void Deactivate_ShouldSetStatusToInactive()
    {
        var product = TestDataFactory.CreateMenProduct();
        product.Deactivate();
        product.Status.Should().Be(ProductStatus.Inactive);
    }

    [Fact]
    public void AddReview_ShouldUpdateRatingAndReviewCount()
    {
        var product = TestDataFactory.CreateMenProduct();
        product.AddReview(5.0);
        product.AddReview(3.0);
        product.ReviewCount.Should().Be(2);
        product.Rating.Should().BeApproximately(4.0, 0.01);
    }

    [Fact]
    public void GetPrice_ShouldReturnMoneyValueObject()
    {
        var product = TestDataFactory.CreateMenProduct();
        var money = product.GetPrice();
        money.Amount.Should().Be(999.99m);
        money.Currency.Should().Be("USD");
    }

    [Fact]
    public void ClearDomainEvents_ShouldRemoveAllEvents()
    {
        var product = TestDataFactory.CreateMenProduct();
        product.DomainEvents.Should().NotBeEmpty();
        product.ClearDomainEvents();
        product.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Men", "Shirts")]
    [InlineData("Women", "Dresses")]
    [InlineData("Kids", "T-Shirts")]
    [InlineData("Sports", "Jerseys")]
    [InlineData("Formal", "Suits")]
    public void Create_ShouldSupportAnyCategory(string category, string subCategory)
    {
        var product = Product.Create("Test", "Desc", $"SKU-{category}", "Brand",
            category, subCategory, 100m, "USD", 10, ["M"], ["White"], null);
        product.CategoryName.Should().Be(category);
        product.SubCategoryName.Should().Be(subCategory);
    }
}
