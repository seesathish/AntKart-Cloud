using AK.Products.Domain.Entities;
using AK.Products.Domain.Enums;
using AK.Products.Domain.Specifications;
using AK.Products.Tests.Common;
using FluentAssertions;

namespace AK.Products.Tests.Domain.Specifications;

public sealed class SpecificationTests
{
    [Fact]
    public void ActiveProductsSpecification_ShouldMatchActiveProduct()
    {
        var spec = new ActiveProductsSpecification();
        var product = TestDataFactory.CreateMenProduct();
        spec.Criteria.Compile()(product).Should().BeTrue();
    }

    [Fact]
    public void ActiveProductsSpecification_ShouldNotMatchInactiveProduct()
    {
        var spec = new ActiveProductsSpecification();
        var product = TestDataFactory.CreateMenProduct();
        product.Deactivate();
        spec.Criteria.Compile()(product).Should().BeFalse();
    }

    [Fact]
    public void FeaturedProductsSpecification_ShouldMatchFeaturedActiveProduct()
    {
        var spec = new FeaturedProductsSpecification();
        var product = TestDataFactory.CreateMenProduct();
        product.SetFeatured(true);
        spec.Criteria.Compile()(product).Should().BeTrue();
    }

    [Fact]
    public void FeaturedProductsSpecification_ShouldNotMatchNonFeaturedProduct()
    {
        var spec = new FeaturedProductsSpecification();
        var product = TestDataFactory.CreateMenProduct();
        spec.Criteria.Compile()(product).Should().BeFalse();
    }

    [Fact]
    public void FeaturedProductsSpecification_ShouldNotMatchFeaturedInactiveProduct()
    {
        var spec = new FeaturedProductsSpecification();
        var product = TestDataFactory.CreateMenProduct();
        product.SetFeatured(true);
        product.Deactivate();
        spec.Criteria.Compile()(product).Should().BeFalse();
    }

    [Fact]
    public void ProductByCategorySpecification_ShouldMatchCorrectCategory()
    {
        var spec = new ProductByCategorySpecification("Men");
        var product = TestDataFactory.CreateMenProduct();
        spec.Criteria.Compile()(product).Should().BeTrue();
    }

    [Fact]
    public void ProductByCategorySpecification_ShouldNotMatchDifferentCategory()
    {
        var spec = new ProductByCategorySpecification("Women");
        var product = TestDataFactory.CreateMenProduct();
        spec.Criteria.Compile()(product).Should().BeFalse();
    }

    [Fact]
    public void ProductByIdSpecification_ShouldMatchProductById()
    {
        var product = TestDataFactory.CreateMenProduct();
        var spec = new ProductByIdSpecification(product.Id);
        spec.Criteria.Compile()(product).Should().BeTrue();
    }

    [Fact]
    public void ProductByIdSpecification_ShouldNotMatchDifferentId()
    {
        var product = TestDataFactory.CreateMenProduct();
        var spec = new ProductByIdSpecification("different-id");
        spec.Criteria.Compile()(product).Should().BeFalse();
    }

    [Fact]
    public void ProductSearchSpecification_WithNoFilters_ShouldMatchAnyProduct()
    {
        var spec = new ProductSearchSpecification(null, null, null, null);
        var product = TestDataFactory.CreateMenProduct();
        spec.Criteria.Compile()(product).Should().BeTrue();
    }

    [Fact]
    public void ProductSearchSpecification_WithMatchingSearchTerm_ShouldMatchByName()
    {
        var spec = new ProductSearchSpecification("Shirt", null, null, null);
        var product = TestDataFactory.CreateMenProduct();
        spec.Criteria.Compile()(product).Should().BeTrue();
    }

    [Fact]
    public void ProductSearchSpecification_WithMatchingSearchTerm_ShouldMatchBySku()
    {
        var spec = new ProductSearchSpecification("MEN-SHRT", null, null, null);
        var product = TestDataFactory.CreateMenProduct();
        spec.Criteria.Compile()(product).Should().BeTrue();
    }

    [Fact]
    public void ProductSearchSpecification_WithNonMatchingSearchTerm_ShouldNotMatch()
    {
        var spec = new ProductSearchSpecification("Dress", null, null, null);
        var product = TestDataFactory.CreateMenProduct();
        spec.Criteria.Compile()(product).Should().BeFalse();
    }

    [Fact]
    public void ProductSearchSpecification_WithCategoryFilter_ShouldFilterByCategory()
    {
        var spec = new ProductSearchSpecification(null, "Men", null, null);
        var product = TestDataFactory.CreateMenProduct();
        spec.Criteria.Compile()(product).Should().BeTrue();
    }

    [Fact]
    public void ProductSearchSpecification_WithSubCategoryFilter_ShouldFilterBySubCategory()
    {
        var spec = new ProductSearchSpecification(null, null, "Shirts", null);
        var product = TestDataFactory.CreateMenProduct();
        spec.Criteria.Compile()(product).Should().BeTrue();
    }

    [Fact]
    public void ProductSearchSpecification_WithNonMatchingSubCategory_ShouldNotMatch()
    {
        var spec = new ProductSearchSpecification(null, null, "Dresses", null);
        var product = TestDataFactory.CreateMenProduct();
        spec.Criteria.Compile()(product).Should().BeFalse();
    }

    [Fact]
    public void ProductSearchSpecification_WithStatusFilter_ShouldFilterByStatus()
    {
        var spec = new ProductSearchSpecification(null, null, null, ProductStatus.Inactive);
        var product = TestDataFactory.CreateMenProduct();
        spec.Criteria.Compile()(product).Should().BeFalse();
    }

    [Fact]
    public void ProductSearchSpecification_ShouldEnablePaging()
    {
        var spec = new ProductSearchSpecification(null, null, null, null, skip: 10, take: 20);
        spec.IsPagingEnabled.Should().BeTrue();
        spec.Skip.Should().Be(10);
        spec.Take.Should().Be(20);
    }

    [Fact]
    public void ProductSearchSpecification_ShouldSetOrderByDescending()
    {
        var spec = new ProductSearchSpecification(null, null, null, null);
        spec.OrderByDescending.Should().NotBeNull();
    }
}
