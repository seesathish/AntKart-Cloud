using AK.BuildingBlocks.Common;
using FluentAssertions;

namespace AK.Products.Tests.BuildingBlocks;

public sealed class ResultTests
{
    [Fact]
    public void Success_ShouldSetIsSuccessTrue()
    {
        var result = Result<string>.Success("value");
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("value");
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Failure_ShouldSetIsSuccessFalse()
    {
        var result = Result<string>.Failure("Something went wrong");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Something went wrong");
        result.Value.Should().BeNull();
    }

    [Fact]
    public void Success_WithComplexType_ShouldStoreValue()
    {
        var result = Result<int>.Success(42);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }
}
