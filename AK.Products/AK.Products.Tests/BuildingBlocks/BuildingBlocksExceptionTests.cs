using AK.BuildingBlocks.Exceptions;
using FluentAssertions;

namespace AK.Products.Tests.BuildingBlocks;

public sealed class BuildingBlocksExceptionTests
{
    [Fact]
    public void NotFoundException_ShouldFormatMessageWithNameAndKey()
    {
        var ex = new NotFoundException("Product", "abc123");
        ex.Message.Should().Contain("Product");
        ex.Message.Should().Contain("abc123");
    }

    [Fact]
    public void NotFoundException_ShouldInheritFromException()
    {
        var ex = new NotFoundException("Order", 99);
        ex.Should().BeAssignableTo<Exception>();
    }

    [Fact]
    public void ValidationException_ShouldStoreErrors()
    {
        var errors = new[] { "Name is required", "Price must be positive" };
        var ex = new AK.BuildingBlocks.Exceptions.ValidationException(errors);
        ex.Errors.Should().HaveCount(2);
        ex.Errors.Should().Contain("Name is required");
        ex.Errors.Should().Contain("Price must be positive");
    }

    [Fact]
    public void ValidationException_ShouldHaveFixedMessage()
    {
        var ex = new AK.BuildingBlocks.Exceptions.ValidationException(["error"]);
        ex.Message.Should().Be("One or more validation failures occurred.");
    }

    [Fact]
    public void ValidationException_ShouldInheritFromException()
    {
        var ex = new AK.BuildingBlocks.Exceptions.ValidationException(["err"]);
        ex.Should().BeAssignableTo<Exception>();
    }
}
