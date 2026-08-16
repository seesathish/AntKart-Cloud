using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Products.Application.Consumers;
using AK.Products.Application.Interfaces;
using AK.Products.Domain.Entities;
using AK.Products.Tests.Common;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;

namespace AK.Products.Tests.Consumers;

public sealed class ReserveStockConsumerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IProductRepository> _products = new();
    private readonly Mock<ILogger<ReserveStockConsumer>> _logger = new();
    private readonly Mock<ConsumeContext<OrderCreatedIntegrationEvent>> _context = new();

    public ReserveStockConsumerTests()
    {
        _uow.Setup(u => u.Products).Returns(_products.Object);
        _context.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
    }

    private ReserveStockConsumer CreateConsumer() => new(_uow.Object, _logger.Object);

    private static OrderCreatedIntegrationEvent MakeEvent(
        Guid orderId, string userId,
        params (string productId, string sku, int qty)[] items)
    {
        var payloads = items.Select(i => new OrderItemPayload(i.productId, i.sku, i.qty, 10m)).ToList();
        return new OrderCreatedIntegrationEvent(orderId, userId, "test@example.com", "Test User", "ORD-TEST-001", payloads, payloads.Sum(p => p.UnitPrice * p.Quantity));
    }

    [Fact]
    public async Task Consume_WhenSufficientStock_PublishesStockReservedEvent()
    {
        var product = TestDataFactory.CreateProduct(stock: 10);
        _products.Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _context.Setup(c => c.Message)
            .Returns(MakeEvent(Guid.NewGuid(), "user1", (product.Id, product.SKU, 2)));

        await CreateConsumer().Consume(_context.Object);

        _context.Verify(c => c.Publish(It.IsAny<StockReservedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_WhenSufficientStock_DoesNotPublishFailedEvent()
    {
        var product = TestDataFactory.CreateProduct(stock: 10);
        _products.Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _context.Setup(c => c.Message)
            .Returns(MakeEvent(Guid.NewGuid(), "user1", (product.Id, product.SKU, 2)));

        await CreateConsumer().Consume(_context.Object);

        _context.Verify(c => c.Publish(It.IsAny<StockReservationFailedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_WhenInsufficientStock_PublishesStockReservationFailed()
    {
        var product = TestDataFactory.CreateProduct(stock: 1);
        _products.Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _context.Setup(c => c.Message)
            .Returns(MakeEvent(Guid.NewGuid(), "user1", (product.Id, product.SKU, 5)));

        await CreateConsumer().Consume(_context.Object);

        _context.Verify(c => c.Publish(It.IsAny<StockReservationFailedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_WhenInsufficientStock_DoesNotDecrementStock()
    {
        var product = TestDataFactory.CreateProduct(stock: 1);
        _products.Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _context.Setup(c => c.Message)
            .Returns(MakeEvent(Guid.NewGuid(), "user1", (product.Id, product.SKU, 5)));

        await CreateConsumer().Consume(_context.Object);

        _products.Verify(r => r.UpdateAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_WhenProductNotFound_PublishesStockReservationFailed()
    {
        _products.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);
        _context.Setup(c => c.Message)
            .Returns(MakeEvent(Guid.NewGuid(), "user1", ("missing-id", "MEN-SHIR-999", 1)));

        await CreateConsumer().Consume(_context.Object);

        _context.Verify(c => c.Publish(It.IsAny<StockReservationFailedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_WhenSufficientStock_DecrementsStockAndSaves()
    {
        var product = TestDataFactory.CreateProduct(stock: 10);
        _products.Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _context.Setup(c => c.Message)
            .Returns(MakeEvent(Guid.NewGuid(), "user1", (product.Id, product.SKU, 3)));

        await CreateConsumer().Consume(_context.Object);

        product.StockQuantity.Should().Be(7);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_WhenSufficientStock_UpdatesEachItem()
    {
        var product1 = TestDataFactory.CreateProduct(sku: "MEN-SHRT-001", stock: 10);
        var product2 = TestDataFactory.CreateProduct(sku: "MEN-SHRT-002", stock: 10);
        _products.Setup(r => r.GetByIdAsync(product1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product1);
        _products.Setup(r => r.GetByIdAsync(product2.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product2);
        _context.Setup(c => c.Message)
            .Returns(MakeEvent(Guid.NewGuid(), "user1",
                (product1.Id, product1.SKU, 1),
                (product2.Id, product2.SKU, 2)));

        await CreateConsumer().Consume(_context.Object);

        _products.Verify(r => r.UpdateAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Consume_WhenPartialStockInsufficient_FailsForAllItems()
    {
        var product1 = TestDataFactory.CreateProduct(sku: "MEN-SHRT-001", stock: 10);
        var product2 = TestDataFactory.CreateProduct(sku: "MEN-SHRT-002", stock: 1);
        _products.Setup(r => r.GetByIdAsync(product1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product1);
        _products.Setup(r => r.GetByIdAsync(product2.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product2);
        _context.Setup(c => c.Message)
            .Returns(MakeEvent(Guid.NewGuid(), "user1",
                (product1.Id, product1.SKU, 2),
                (product2.Id, product2.SKU, 5)));

        await CreateConsumer().Consume(_context.Object);

        _context.Verify(c => c.Publish(It.IsAny<StockReservationFailedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _context.Verify(c => c.Publish(It.IsAny<StockReservedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
