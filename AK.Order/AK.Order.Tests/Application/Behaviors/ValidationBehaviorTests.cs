using AK.BuildingBlocks.Behaviors;
using AK.Order.Application.Features.CreateOrder;
using AK.Order.Tests.Common;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Moq;

namespace AK.Order.Tests.Application.Behaviors;

public class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_NoValidators_CallsNext()
    {
        var behavior = new ValidationBehavior<CreateOrderCommand, object>(
            Enumerable.Empty<IValidator<CreateOrderCommand>>());

        var next = new Mock<RequestHandlerDelegate<object>>();
        next.Setup(n => n()).ReturnsAsync(new object());

        var command = new CreateOrderCommand("user-1", "a@b.com", "Test", TestDataFactory.CreateOrderDto());
        await behavior.Handle(command, next.Object, CancellationToken.None);

        next.Verify(n => n(), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsNext()
    {
        var behavior = new ValidationBehavior<CreateOrderCommand, object>(
            [new CreateOrderValidator()]);

        var next = new Mock<RequestHandlerDelegate<object>>();
        next.Setup(n => n()).ReturnsAsync(new object());

        var command = new CreateOrderCommand("user-1", "a@b.com", "Test", TestDataFactory.CreateOrderDto());
        await behavior.Handle(command, next.Object, CancellationToken.None);

        next.Verify(n => n(), Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidRequest_ThrowsValidationException()
    {
        var behavior = new ValidationBehavior<CreateOrderCommand, object>(
            [new CreateOrderValidator()]);

        var next = new Mock<RequestHandlerDelegate<object>>();
        var command = new CreateOrderCommand("", "a@b.com", "Test", TestDataFactory.CreateOrderDto());

        var act = async () => await behavior.Handle(command, next.Object, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        next.Verify(n => n(), Times.Never);
    }
}
