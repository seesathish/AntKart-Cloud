using AK.Products.Domain.Entities;
using FluentAssertions;

namespace AK.Products.Tests.Domain.Entities;

public sealed class CategoryTests
{
    [Fact]
    public void Create_WithValidData_ShouldCreateCategory()
    {
        var cat = Category.Create("Men", "men", "Men's clothing");
        cat.Name.Should().Be("Men");
        cat.Slug.Should().Be("men");
        cat.Description.Should().Be("Men's clothing");
        cat.IsActive.Should().BeTrue();
        cat.ParentCategoryId.Should().BeNull();
    }

    [Fact]
    public void Create_ShouldLowercaseSlug()
    {
        var cat = Category.Create("Men", "MEN-CLOTHING");
        cat.Slug.Should().Be("men-clothing");
    }

    [Fact]
    public void Create_WithParentCategoryId_ShouldSetParent()
    {
        var cat = Category.Create("Shirts", "shirts", null, "parent-123");
        cat.ParentCategoryId.Should().Be("parent-123");
    }

    [Fact]
    public void Create_WithEmptyName_ShouldThrow()
    {
        var act = () => Category.Create("", "shirts");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithEmptySlug_ShouldThrow()
    {
        var act = () => Category.Create("Shirts", "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_ShouldUpdateAllProperties()
    {
        var cat = Category.Create("Men", "men");
        cat.Update("Sports", "sports", "Sportswear");
        cat.Name.Should().Be("Sports");
        cat.Slug.Should().Be("sports");
        cat.Description.Should().Be("Sportswear");
    }

    [Fact]
    public void Update_ShouldLowercaseSlug()
    {
        var cat = Category.Create("Men", "men");
        cat.Update("Men", "MEN-UPDATED", null);
        cat.Slug.Should().Be("men-updated");
    }

    [Fact]
    public void Deactivate_ShouldSetIsActiveFalse()
    {
        var cat = Category.Create("Men", "men");
        cat.Deactivate();
        cat.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Activate_AfterDeactivate_ShouldSetIsActiveTrue()
    {
        var cat = Category.Create("Men", "men");
        cat.Deactivate();
        cat.Activate();
        cat.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("Men")]
    [InlineData("Women")]
    [InlineData("Kids")]
    [InlineData("Sports")]
    [InlineData("Formal")]
    public void Create_ShouldSupportAnyCategory(string categoryName)
    {
        var cat = Category.Create(categoryName, categoryName.ToLower());
        cat.Name.Should().Be(categoryName);
        cat.IsActive.Should().BeTrue();
    }
}
