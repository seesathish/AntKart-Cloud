using AK.Products.Domain.Common;
using AK.Products.Domain.Entities;
using FluentAssertions;

namespace AK.Products.Tests.Domain.Specifications;

file sealed class TestSpecification : BaseSpecification<Product>
{
    public TestSpecification(System.Linq.Expressions.Expression<Func<Product, bool>> criteria)
        : base(criteria) { }

    public void AddIncludePublic(System.Linq.Expressions.Expression<Func<Product, object>> expr)
        => AddInclude(expr);

    public void ApplyOrderByPublic(System.Linq.Expressions.Expression<Func<Product, object>> expr)
        => ApplyOrderBy(expr);

    public void ApplyOrderByDescPublic(System.Linq.Expressions.Expression<Func<Product, object>> expr)
        => ApplyOrderByDescending(expr);

    public void ApplyPagingPublic(int skip, int take) => ApplyPaging(skip, take);
}

public sealed class CommonBaseSpecificationTests
{
    [Fact]
    public void Criteria_ShouldBeSetFromConstructor()
    {
        var spec = new TestSpecification(p => p.Name == "Test");
        spec.Criteria.Should().NotBeNull();
    }

    [Fact]
    public void AddInclude_ShouldAddToIncludesList()
    {
        var spec = new TestSpecification(p => true);
        spec.AddIncludePublic(p => p.Name);
        spec.Includes.Should().HaveCount(1);
    }

    [Fact]
    public void ApplyOrderBy_ShouldSetOrderByExpression()
    {
        var spec = new TestSpecification(p => true);
        spec.ApplyOrderByPublic(p => p.Name);
        spec.OrderBy.Should().NotBeNull();
        spec.OrderByDescending.Should().BeNull();
    }

    [Fact]
    public void ApplyOrderByDescending_ShouldSetOrderByDescExpression()
    {
        var spec = new TestSpecification(p => true);
        spec.ApplyOrderByDescPublic(p => p.Price);
        spec.OrderByDescending.Should().NotBeNull();
        spec.OrderBy.Should().BeNull();
    }

    [Fact]
    public void ApplyPaging_ShouldSetSkipAndTake()
    {
        var spec = new TestSpecification(p => true);
        spec.ApplyPagingPublic(10, 20);
        spec.Skip.Should().Be(10);
        spec.Take.Should().Be(20);
    }
}
