using AK.Order.Application.Common.DTOs;
using AK.Order.Application.Features.CreateOrder;
using AK.Order.Tests.Common;
using FluentAssertions;

namespace AK.Order.Tests.Features.CreateOrder;

public class CreateOrderValidatorTests
{
    private readonly CreateOrderValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_PassesValidation()
    {
        var command = new CreateOrderCommand("user-123", "a@b.com", "Test", TestDataFactory.CreateOrderDto());
        var result = _validator.Validate(command);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyUserId_FailsValidation()
    {
        var command = new CreateOrderCommand("", "a@b.com", "Test", TestDataFactory.CreateOrderDto());
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "UserId");
    }

    [Fact]
    public void Validate_UserIdTooLong_FailsValidation()
    {
        var command = new CreateOrderCommand(new string('x', 101), "a@b.com", "Test", TestDataFactory.CreateOrderDto());
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_NoItems_FailsValidation()
    {
        var dto = new CreateOrderDto(
            TestDataFactory.CreateOrderDto().ShippingAddress,
            [],
            null);
        var command = new CreateOrderCommand("user-1", "a@b.com", "Test", dto);
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("at least one item"));
    }

    [Fact]
    public void Validate_ItemWithZeroPrice_FailsValidation()
    {
        var dto = new CreateOrderDto(
            TestDataFactory.CreateOrderDto().ShippingAddress,
            [new CreateOrderItemDto("p1", "Product", "SKU", 0m, 1, null)],
            null);
        var command = new CreateOrderCommand("user-1", "a@b.com", "Test", dto);
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ItemWithZeroQuantity_FailsValidation()
    {
        var dto = new CreateOrderDto(
            TestDataFactory.CreateOrderDto().ShippingAddress,
            [new CreateOrderItemDto("p1", "Product", "SKU", 10m, 0, null)],
            null);
        var command = new CreateOrderCommand("user-1", "a@b.com", "Test", dto);
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyShippingCity_FailsValidation()
    {
        var addr = new ShippingAddressDto("John", "123 Main St", null, "", "IL", "62701", "US", "+1-555");
        var dto = new CreateOrderDto(addr, TestDataFactory.CreateOrderDto().Items, null);
        var command = new CreateOrderCommand("user-1", "a@b.com", "Test", dto);
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyProductId_FailsValidation()
    {
        var dto = new CreateOrderDto(
            TestDataFactory.CreateOrderDto().ShippingAddress,
            [new CreateOrderItemDto("", "Product", "SKU", 10m, 1, null)],
            null);
        var command = new CreateOrderCommand("user-1", "a@b.com", "Test", dto);
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
    }
}
