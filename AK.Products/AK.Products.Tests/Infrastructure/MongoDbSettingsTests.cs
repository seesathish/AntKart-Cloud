using AK.Products.Infrastructure.Persistence;
using FluentAssertions;

namespace AK.Products.Tests.Infrastructure;

public sealed class MongoDbSettingsTests
{
    [Fact]
    public void DefaultConnectionString_ShouldBeLocalhost()
    {
        var settings = new MongoDbSettings();
        settings.ConnectionString.Should().Be("mongodb://localhost:27017");
    }

    [Fact]
    public void DefaultDatabaseName_ShouldBeACProductsDb()
    {
        var settings = new MongoDbSettings();
        settings.DatabaseName.Should().Be("ACProductsDb");
    }

    [Fact]
    public void DefaultProductsCollection_ShouldBeProducts()
    {
        var settings = new MongoDbSettings();
        settings.ProductsCollection.Should().Be("products");
    }

    [Fact]
    public void Properties_CanBeSetAndRead()
    {
        var settings = new MongoDbSettings
        {
            ConnectionString = "mongodb://testhost:27017",
            DatabaseName = "TestDb",
            ProductsCollection = "TestCollection"
        };

        settings.ConnectionString.Should().Be("mongodb://testhost:27017");
        settings.DatabaseName.Should().Be("TestDb");
        settings.ProductsCollection.Should().Be("TestCollection");
    }
}
