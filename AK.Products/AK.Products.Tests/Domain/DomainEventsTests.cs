using AK.Products.Domain.Events;
using FluentAssertions;

namespace AK.Products.Tests.Domain;

public sealed class DomainEventsTests
{
    [Fact]
    public void ProductCreatedEvent_ShouldHoldProductIdAndName()
    {
        var evt = new ProductCreatedEvent("product-123", "Test Product");
        evt.ProductId.Should().Be("product-123");
        evt.ProductName.Should().Be("Test Product");
    }

    [Fact]
    public void ProductUpdatedEvent_ShouldHoldProductIdAndName()
    {
        var evt = new ProductUpdatedEvent("product-456", "Updated Product");
        evt.ProductId.Should().Be("product-456");
        evt.ProductName.Should().Be("Updated Product");
    }

    [Fact]
    public void ProductDeletedEvent_ShouldHoldProductId()
    {
        var evt = new ProductDeletedEvent("product-789");
        evt.ProductId.Should().Be("product-789");
    }

    [Fact]
    public void ProductCreatedEvent_EqualityByValue_ShouldWork()
    {
        var evt1 = new ProductCreatedEvent("product-123", "Test Product");
        var evt2 = new ProductCreatedEvent("product-123", "Test Product");
        evt1.Should().Be(evt2);
    }

    [Fact]
    public void ProductDeletedEvent_EqualityByValue_ShouldWork()
    {
        var evt1 = new ProductDeletedEvent("product-789");
        var evt2 = new ProductDeletedEvent("product-789");
        evt1.Should().Be(evt2);
    }
}
