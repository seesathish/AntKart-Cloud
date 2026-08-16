using AK.Order.Domain.Enums;
using AK.Order.Domain.Specifications;
using AK.Order.Tests.Common;
using FluentAssertions;

namespace AK.Order.Tests.Domain;

public class SpecificationTests
{
    [Fact]
    public void OrderByIdSpecification_MatchesCorrectOrder()
    {
        var order = TestDataFactory.CreateOrder();
        var spec = new OrderByIdSpecification(order.Id);
        var compiled = spec.Criteria.Compile();
        compiled(order).Should().BeTrue();
    }

    [Fact]
    public void OrderByIdSpecification_DoesNotMatchDifferentId()
    {
        var order = TestDataFactory.CreateOrder();
        var spec = new OrderByIdSpecification(Guid.NewGuid());
        var compiled = spec.Criteria.Compile();
        compiled(order).Should().BeFalse();
    }

    [Fact]
    public void OrdersByUserSpecification_MatchesCorrectUser()
    {
        var order = TestDataFactory.CreateOrder(userId: "user-abc");
        var spec = new OrdersByUserSpecification("user-abc");
        var compiled = spec.Criteria.Compile();
        compiled(order).Should().BeTrue();
    }

    [Fact]
    public void OrdersByUserSpecification_HasOrderByDescending()
    {
        var spec = new OrdersByUserSpecification("user-1");
        spec.OrderByDescending.Should().NotBeNull();
        spec.OrderBy.Should().BeNull();
    }

    [Fact]
    public void OrdersByStatusSpecification_MatchesCorrectStatus()
    {
        var order = TestDataFactory.CreateOrder();
        var spec = new OrdersByStatusSpecification(OrderStatus.Pending);
        var compiled = spec.Criteria.Compile();
        compiled(order).Should().BeTrue();
    }

    [Fact]
    public void OrdersByStatusSpecification_DoesNotMatchDifferentStatus()
    {
        var order = TestDataFactory.CreateOrder();
        var spec = new OrdersByStatusSpecification(OrderStatus.Shipped);
        var compiled = spec.Criteria.Compile();
        compiled(order).Should().BeFalse();
    }

    [Fact]
    public void OrdersPagedSpecification_NoFilter_MatchesAnyOrder()
    {
        var order = TestDataFactory.CreateOrder();
        var spec = new OrdersPagedSpecification(1, 20);
        var compiled = spec.Criteria.Compile();
        compiled(order).Should().BeTrue();
    }

    [Fact]
    public void OrdersPagedSpecification_WithUserFilter_FiltersCorrectly()
    {
        var order = TestDataFactory.CreateOrder(userId: "user-xyz");
        var spec = new OrdersPagedSpecification(1, 20, "user-xyz");
        var compiled = spec.Criteria.Compile();
        compiled(order).Should().BeTrue();

        var specOther = new OrdersPagedSpecification(1, 20, "user-other");
        compiled = specOther.Criteria.Compile();
        compiled(order).Should().BeFalse();
    }

    [Fact]
    public void OrdersPagedSpecification_AppliesPaging()
    {
        var spec = new OrdersPagedSpecification(2, 10);
        spec.Skip.Should().Be(10);
        spec.Take.Should().Be(10);
    }

    [Fact]
    public void OrdersPagedSpecification_WithStatusFilter_FiltersCorrectly()
    {
        var order = TestDataFactory.CreateOrder();
        var spec = new OrdersPagedSpecification(1, 20, null, OrderStatus.Pending);
        var compiled = spec.Criteria.Compile();
        compiled(order).Should().BeTrue();

        var specShipped = new OrdersPagedSpecification(1, 20, null, OrderStatus.Shipped);
        compiled = specShipped.Criteria.Compile();
        compiled(order).Should().BeFalse();
    }
}
