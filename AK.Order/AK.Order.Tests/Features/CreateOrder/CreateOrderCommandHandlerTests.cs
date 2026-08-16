using AK.BuildingBlocks.Messaging.EventGrid;
using AK.BuildingBlocks.Messaging.Notifications;
using AK.Order.Application.Common.Exceptions;
using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Features.CreateOrder;
using AK.Order.Tests.Common;
using MassTransit;
using OrderEntity = AK.Order.Domain.Entities.Order;
using FluentAssertions;
using Moq;

namespace AK.Order.Tests.Features.CreateOrder;

public class CreateOrderCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IOrderRepository> _repo = new();
    private readonly Mock<IPublishEndpoint> _publisher = new();
    private readonly Mock<IEventGridSideEffectPublisher> _sideEffects = new();
    private readonly Mock<ICatalogPriceProvider> _catalog = new();

    // The TestDataFactory order contains one line: prod-001 @ 29.99 x2.
    private const string ProductId = "prod-001";
    private const decimal SubmittedPrice = 29.99m;

    public CreateOrderCommandHandlerTests()
    {
        _uow.Setup(u => u.Orders).Returns(_repo.Object);
        _repo.Setup(r => r.AddAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderEntity o, CancellationToken _) => o);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _sideEffects.Setup(s => s.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Default: catalogue agrees with the submitted price (effective == submitted), so the
        // happy path proceeds. Individual tests override this.
        SetCatalog(new CatalogPriceResult(CatalogPriceStatus.Found, SubmittedPrice));
    }

    private void SetCatalog(CatalogPriceResult result) =>
        _catalog.Setup(c => c.GetEffectivePricesAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, CatalogPriceResult> { [ProductId] = result });

    private CreateOrderCommandHandler Handler() =>
        new(_uow.Object, _publisher.Object, _sideEffects.Object, _catalog.Object);

    private CreateOrderCommand BuildCommand(
        string userId = "user-123",
        string email = "john@example.com",
        string name = "John Doe")
        => new(userId, email, name, TestDataFactory.CreateOrderDto());

    private void VerifyNothingPersisted()
    {
        _repo.Verify(r => r.AddAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Existing behaviour (adapted to CreateOrderResult) ────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithOrderDto()
    {
        var result = await Handler().Handle(BuildCommand(), CancellationToken.None);

        result.Status.Should().Be(CreateOrderStatus.Success);
        result.Order.Should().NotBeNull();
        result.Order!.UserId.Should().Be("user-123");
        result.Order.OrderNumber.Should().StartWith("ORD-");
        result.Order.Status.Should().Be("Pending");
        result.Order.PaymentStatus.Should().Be("Pending");
        result.Order.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_ValidCommand_SavesOrder()
    {
        await Handler().Handle(BuildCommand(), CancellationToken.None);

        _repo.Verify(r => r.AddAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCommand_PublishesOrderCreatedEvent()
    {
        await Handler().Handle(BuildCommand("user-123", "john@example.com", "John Doe"), CancellationToken.None);

        _publisher.Verify(p => p.Publish(
            It.Is<AK.BuildingBlocks.Messaging.IntegrationEvents.OrderCreatedIntegrationEvent>(e =>
                e.UserId == "user-123" &&
                e.CustomerEmail == "john@example.com" &&
                e.CustomerName == "John Doe" &&
                e.OrderNumber.StartsWith("ORD-")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCommand_MapsShippingAddress()
    {
        var result = await Handler().Handle(BuildCommand(), CancellationToken.None);

        result.Order!.ShippingAddress.FullName.Should().Be("John Doe");
        result.Order.ShippingAddress.City.Should().Be("Springfield");
    }

    [Fact]
    public async Task Handle_ValidCommand_StoresCustomerEmail()
    {
        var result = await Handler().Handle(BuildCommand(email: "customer@test.com"), CancellationToken.None);

        result.Order.Should().NotBeNull();
        _publisher.Verify(p => p.Publish(
            It.Is<AK.BuildingBlocks.Messaging.IntegrationEvents.OrderCreatedIntegrationEvent>(e =>
                e.CustomerEmail == "customer@test.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AfterCommit_PublishesOrderCreatedNotificationSideEffect()
    {
        OrderCreatedNotification? published = null;
        _sideEffects.Setup(s => s.TryPublishAsync(
                NotificationEventTypes.OrderCreated, It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, object, CancellationToken>((_, _, data, _) => published = (OrderCreatedNotification)data)
            .ReturnsAsync(true);

        await Handler().Handle(BuildCommand(email: "customer@test.com", name: "John Doe"), CancellationToken.None);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        published.Should().NotBeNull();
        published!.CustomerEmail.Should().Be("customer@test.com");
        published.CustomerName.Should().Be("John Doe");
        published.OrderNumber.Should().StartWith("ORD-");
    }

    [Fact]
    public async Task Handle_WhenNotificationPublishFails_DoesNotFailTheOrder()
    {
        _sideEffects.Setup(s => s.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Handler().Handle(BuildCommand(), CancellationToken.None);

        result.Status.Should().Be(CreateOrderStatus.Success);
        result.Order!.OrderNumber.Should().StartWith("ORD-");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Price revalidation (asymmetric, server-authoritative) ────────────────────────────────────

    [Fact]
    public async Task AllMatch_Proceeds_PersistsAndPublishes()
    {
        var result = await Handler().Handle(BuildCommand(), CancellationToken.None);

        result.Status.Should().Be(CreateOrderStatus.Success);
        _repo.Verify(r => r.AddAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(p => p.Publish(
            It.IsAny<AK.BuildingBlocks.Messaging.IntegrationEvents.OrderCreatedIntegrationEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PriceDrop_AppliesLowerPrice_Proceeds()
    {
        // Catalogue effective price is LOWER than submitted (a drop) — accept and charge the lower price.
        SetCatalog(new CatalogPriceResult(CatalogPriceStatus.Found, 19.99m));
        OrderEntity? captured = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>()))
            .Callback<OrderEntity, CancellationToken>((o, _) => captured = o)
            .ReturnsAsync((OrderEntity o, CancellationToken _) => o);

        var result = await Handler().Handle(BuildCommand(), CancellationToken.None);

        result.Status.Should().Be(CreateOrderStatus.Success);
        captured.Should().NotBeNull();
        captured!.Items.Single().Price.Should().Be(19.99m);     // persisted at the effective price
        result.Order!.Items.Single().Price.Should().Be(19.99m);
    }

    [Fact]
    public async Task PriceIncrease_Returns_PriceChanged_NoPersist()
    {
        SetCatalog(new CatalogPriceResult(CatalogPriceStatus.Found, 39.99m)); // higher than submitted 29.99

        var result = await Handler().Handle(BuildCommand(), CancellationToken.None);

        result.Status.Should().Be(CreateOrderStatus.PriceChanged);
        var problem = result.Problems.Single();
        problem.ProductId.Should().Be(ProductId);
        problem.Reason.Should().Be("PriceIncreased");
        problem.SubmittedPrice.Should().Be(SubmittedPrice);
        problem.CurrentPrice.Should().Be(39.99m);
        VerifyNothingPersisted();
    }

    [Fact]
    public async Task ProductNotFound_Returns_ProductUnavailable_NoPersist()
    {
        SetCatalog(new CatalogPriceResult(CatalogPriceStatus.NotFound, 0m));

        var result = await Handler().Handle(BuildCommand(), CancellationToken.None);

        result.Status.Should().Be(CreateOrderStatus.ProductUnavailable);
        var problem = result.Problems.Single();
        problem.Reason.Should().Be("ProductNotFound");
        problem.CurrentPrice.Should().BeNull();
        VerifyNothingPersisted();
    }

    [Fact]
    public async Task ProductInactive_Returns_ProductUnavailable_NoPersist()
    {
        SetCatalog(new CatalogPriceResult(CatalogPriceStatus.Inactive, 29.99m));

        var result = await Handler().Handle(BuildCommand(), CancellationToken.None);

        result.Status.Should().Be(CreateOrderStatus.ProductUnavailable);
        result.Problems.Single().Reason.Should().Be("ProductInactive");
        VerifyNothingPersisted();
    }

    [Fact]
    public async Task ProductsUnreachable_FailsClosed_PricingUnavailable_NoPersist()
    {
        _catalog.Setup(c => c.GetEffectivePricesAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CatalogUnavailableException("catalogue down"));

        var result = await Handler().Handle(BuildCommand(), CancellationToken.None);

        result.Status.Should().Be(CreateOrderStatus.PricingUnavailable);
        VerifyNothingPersisted();
    }

    [Fact]
    public async Task DiscountPriceIsAuthoritative()
    {
        // The catalogue effective price is the discount price (19.99). The client submitted the base
        // price (29.99) — treated as a drop — so the order proceeds and is charged the discount price.
        SetCatalog(new CatalogPriceResult(CatalogPriceStatus.Found, 19.99m));
        OrderEntity? captured = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>()))
            .Callback<OrderEntity, CancellationToken>((o, _) => captured = o)
            .ReturnsAsync((OrderEntity o, CancellationToken _) => o);

        var result = await Handler().Handle(BuildCommand(), CancellationToken.None);

        result.Status.Should().Be(CreateOrderStatus.Success);
        captured!.Items.Single().Price.Should().Be(19.99m);
    }
}
