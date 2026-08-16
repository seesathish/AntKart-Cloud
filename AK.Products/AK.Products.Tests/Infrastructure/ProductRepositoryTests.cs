using AK.Products.Domain.Entities;
using AK.Products.Domain.Specifications;
using AK.Products.Infrastructure.Persistence;
using AK.Products.Infrastructure.Persistence.Repositories;
using AK.Products.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;
using Polly.Registry;

namespace AK.Products.Tests.Infrastructure;

public sealed class ProductRepositoryTests
{
    private static Mock<IAsyncCursor<Product>> CreateCursor(IEnumerable<Product> items)
    {
        var list = items.ToList();
        var cursor = new Mock<IAsyncCursor<Product>>();
        cursor.Setup(c => c.Current).Returns(list);
        cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true).ReturnsAsync(false);
        cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>()))
            .Returns(true).Returns(false);
        return cursor;
    }

    private static (ProductRepository repo, Mock<IMongoCollection<Product>> collection) CreateRepo()
    {
        var collection = new Mock<IMongoCollection<Product>>();
        var db = new Mock<IMongoDatabase>();
        db.Setup(d => d.GetCollection<Product>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(collection.Object);
        var context = new MongoDbContext(db.Object);
        var settings = Options.Create(new MongoDbSettings { ProductsCollection = "products" });
        return (new ProductRepository(context, settings, TestResilience.CosmosPipelines()), collection);
    }

    [Fact]
    public async Task AddAsync_ShouldCallInsertOneAndReturnProduct()
    {
        var (repo, collection) = CreateRepo();
        var product = TestDataFactory.CreateMenProduct();
        collection.Setup(c => c.InsertOneAsync(product, It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await repo.AddAsync(product);

        result.Should().Be(product);
        collection.Verify(c => c.InsertOneAsync(product, It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_WhenFound_ShouldReturnProduct()
    {
        var (repo, collection) = CreateRepo();
        var product = TestDataFactory.CreateMenProduct();
        collection.Setup(c => c.FindAsync(
            It.IsAny<FilterDefinition<Product>>(),
            It.IsAny<FindOptions<Product, Product>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCursor([product]).Object);

        var result = await repo.GetByIdAsync(product.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(product.Id);
    }

    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ShouldReturnNull()
    {
        var (repo, collection) = CreateRepo();
        collection.Setup(c => c.FindAsync(
            It.IsAny<FilterDefinition<Product>>(),
            It.IsAny<FindOptions<Product, Product>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCursor([]).Object);

        var result = await repo.GetByIdAsync("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetBySkuAsync_WhenFound_ShouldReturnProduct()
    {
        var (repo, collection) = CreateRepo();
        var product = TestDataFactory.CreateMenProduct("MEN-SHRT-001");
        collection.Setup(c => c.FindAsync(
            It.IsAny<FilterDefinition<Product>>(),
            It.IsAny<FindOptions<Product, Product>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCursor([product]).Object);

        var result = await repo.GetBySkuAsync("MEN-SHRT-001");

        result.Should().NotBeNull();
        result!.SKU.Should().Be("MEN-SHRT-001");
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnAllProducts()
    {
        var (repo, collection) = CreateRepo();
        var products = new List<Product>
        {
            TestDataFactory.CreateMenProduct("SKU-001"),
            TestDataFactory.CreateWomenProduct("SKU-002")
        };
        collection.Setup(c => c.FindAsync(
            It.IsAny<FilterDefinition<Product>>(),
            It.IsAny<FindOptions<Product, Product>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCursor(products).Object);

        var result = await repo.GetAllAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByCategoryAsync_ShouldReturnProductsForCategory()
    {
        var (repo, collection) = CreateRepo();
        var shirts = new List<Product> { TestDataFactory.CreateMenProduct() };
        collection.Setup(c => c.FindAsync(
            It.IsAny<FilterDefinition<Product>>(),
            It.IsAny<FindOptions<Product, Product>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCursor(shirts).Object);

        var result = await repo.GetByCategoryAsync("Shirts");

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetPagedAsync_ShouldReturnPagedProducts()
    {
        var (repo, collection) = CreateRepo();
        var products = new List<Product> { TestDataFactory.CreateMenProduct() };
        collection.Setup(c => c.FindAsync(
            It.IsAny<FilterDefinition<Product>>(),
            It.IsAny<FindOptions<Product, Product>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCursor(products).Object);

        var result = await repo.GetPagedAsync(1, 10);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListAsync_ShouldReturnFilteredProducts()
    {
        var (repo, collection) = CreateRepo();
        var products = new List<Product> { TestDataFactory.CreateMenProduct() };
        collection.Setup(c => c.FindAsync(
            It.IsAny<FilterDefinition<Product>>(),
            It.IsAny<FindOptions<Product, Product>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCursor(products).Object);

        var result = await repo.ListAsync(new ActiveProductsSpecification());

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListAsync_WithSearchSpec_ShouldApplyPagingAndOrdering()
    {
        var (repo, collection) = CreateRepo();
        var products = new List<Product> { TestDataFactory.CreateMenProduct() };
        collection.Setup(c => c.FindAsync(
            It.IsAny<FilterDefinition<Product>>(),
            It.IsAny<FindOptions<Product, Product>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCursor(products).Object);

        var spec = new ProductSearchSpecification("Shirt", "Men", "Shirts", null, skip: 0, take: 10);
        var result = await repo.ListAsync(spec);

        result.Should().HaveCount(1);
        collection.Verify(c => c.FindAsync(
            It.IsAny<FilterDefinition<Product>>(),
            It.IsAny<FindOptions<Product, Product>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CountAsync_ShouldReturnDocumentCount()
    {
        var (repo, collection) = CreateRepo();
        collection.Setup(c => c.CountDocumentsAsync(
            It.IsAny<FilterDefinition<Product>>(),
            It.IsAny<CountOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(5L);

        var result = await repo.CountAsync(new ActiveProductsSpecification());

        result.Should().Be(5);
    }

    [Fact]
    public async Task ExistsAsync_WhenDocumentExists_ShouldReturnTrue()
    {
        var (repo, collection) = CreateRepo();
        collection.Setup(c => c.CountDocumentsAsync(
            It.IsAny<FilterDefinition<Product>>(),
            It.IsAny<CountOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);

        var result = await repo.ExistsAsync("some-id");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WhenDocumentDoesNotExist_ShouldReturnFalse()
    {
        var (repo, collection) = CreateRepo();
        collection.Setup(c => c.CountDocumentsAsync(
            It.IsAny<FilterDefinition<Product>>(),
            It.IsAny<CountOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        var result = await repo.ExistsAsync("nonexistent");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SkuExistsAsync_WhenSkuExists_ShouldReturnTrue()
    {
        var (repo, collection) = CreateRepo();
        collection.Setup(c => c.CountDocumentsAsync(
            It.IsAny<FilterDefinition<Product>>(),
            It.IsAny<CountOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);

        var result = await repo.SkuExistsAsync("MEN-SHRT-001");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SkuExistsAsync_WhenSkuDoesNotExist_ShouldReturnFalse()
    {
        var (repo, collection) = CreateRepo();
        collection.Setup(c => c.CountDocumentsAsync(
            It.IsAny<FilterDefinition<Product>>(),
            It.IsAny<CountOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        var result = await repo.SkuExistsAsync("NONEXISTENT");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_ShouldCallReplaceOne()
    {
        var (repo, collection) = CreateRepo();
        var product = TestDataFactory.CreateMenProduct();
        collection.Setup(c => c.ReplaceOneAsync(
            It.IsAny<FilterDefinition<Product>>(),
            product,
            It.IsAny<ReplaceOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ReplaceOneResult>());

        await repo.UpdateAsync(product);

        collection.Verify(c => c.ReplaceOneAsync(
            It.IsAny<FilterDefinition<Product>>(),
            product,
            It.IsAny<ReplaceOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ShouldCallDeleteOne()
    {
        var (repo, collection) = CreateRepo();
        collection.Setup(c => c.DeleteOneAsync(
            It.IsAny<FilterDefinition<Product>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<DeleteResult>());

        await repo.DeleteAsync("some-id");

        collection.Verify(c => c.DeleteOneAsync(
            It.IsAny<FilterDefinition<Product>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkInsertAsync_ShouldCallInsertMany()
    {
        var (repo, collection) = CreateRepo();
        var products = new List<Product>
        {
            TestDataFactory.CreateMenProduct("SKU-001"),
            TestDataFactory.CreateWomenProduct("SKU-002")
        };
        collection.Setup(c => c.InsertManyAsync(
            It.IsAny<IEnumerable<Product>>(),
            It.IsAny<InsertManyOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await repo.BulkInsertAsync(products);

        collection.Verify(c => c.InsertManyAsync(
            It.IsAny<IEnumerable<Product>>(),
            It.IsAny<InsertManyOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkUpdateAsync_ShouldCallBulkWrite()
    {
        var (repo, collection) = CreateRepo();
        var products = new List<Product> { TestDataFactory.CreateMenProduct() };
        collection.Setup(c => c.BulkWriteAsync(
            It.IsAny<IEnumerable<WriteModel<Product>>>(),
            It.IsAny<BulkWriteOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<BulkWriteResult<Product>>(null!));

        await repo.BulkUpdateAsync(products);

        collection.Verify(c => c.BulkWriteAsync(
            It.IsAny<IEnumerable<WriteModel<Product>>>(),
            It.IsAny<BulkWriteOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
