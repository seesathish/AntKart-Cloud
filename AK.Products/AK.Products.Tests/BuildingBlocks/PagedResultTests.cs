using AK.BuildingBlocks.Common;
using FluentAssertions;

namespace AK.Products.Tests.BuildingBlocks;

public sealed class PagedResultTests
{
    [Fact]
    public void TotalPages_ShouldCalculateCorrectly()
    {
        var result = new PagedResult<string>(["a", "b", "c"], 25, 1, 10);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public void TotalPages_WithExactMultiple_ShouldHaveNoRemainder()
    {
        var result = new PagedResult<string>([], 20, 1, 10);
        result.TotalPages.Should().Be(2);
    }

    [Fact]
    public void HasNextPage_OnFirstPageWithMore_ShouldBeTrue()
    {
        var result = new PagedResult<string>(["a"], 10, 1, 5);
        result.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public void HasNextPage_OnLastPage_ShouldBeFalse()
    {
        var result = new PagedResult<string>(["a"], 10, 2, 5);
        result.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public void HasPreviousPage_OnFirstPage_ShouldBeFalse()
    {
        var result = new PagedResult<string>(["a"], 10, 1, 5);
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void HasPreviousPage_OnSecondPage_ShouldBeTrue()
    {
        var result = new PagedResult<string>(["a"], 10, 2, 5);
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void Properties_ShouldBeSetCorrectly()
    {
        var items = new List<string> { "x", "y" };
        var result = new PagedResult<string>(items, 100, 3, 10);
        result.Items.Should().BeEquivalentTo(items);
        result.TotalCount.Should().Be(100);
        result.Page.Should().Be(3);
        result.PageSize.Should().Be(10);
    }
}
