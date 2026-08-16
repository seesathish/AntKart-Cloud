using AK.BuildingBlocks.Behaviors;
using FluentAssertions;
using FluentValidation;
using MediatR;

namespace AK.ShoppingCart.Tests.Application.Behaviors;

public sealed record CartBehaviorTestRequest(string Value) : IRequest<string>;

public sealed class CartAlwaysPassValidator : AbstractValidator<CartBehaviorTestRequest>
{
    public CartAlwaysPassValidator() { }
}

public sealed class CartAlwaysFailValidator : AbstractValidator<CartBehaviorTestRequest>
{
    public CartAlwaysFailValidator() => RuleFor(x => x.Value).Must(_ => false).WithMessage("Always fails");
}

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_NoValidators_ShouldCallNext()
    {
        var behavior = new ValidationBehavior<CartBehaviorTestRequest, string>([]);
        var nextCalled = false;
        RequestHandlerDelegate<string> next = () => { nextCalled = true; return Task.FromResult("ok"); };

        var result = await behavior.Handle(new CartBehaviorTestRequest("test"), next, default);

        nextCalled.Should().BeTrue();
        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_PassingValidator_ShouldCallNext()
    {
        var behavior = new ValidationBehavior<CartBehaviorTestRequest, string>([new CartAlwaysPassValidator()]);
        var nextCalled = false;
        RequestHandlerDelegate<string> next = () => { nextCalled = true; return Task.FromResult("ok"); };

        var result = await behavior.Handle(new CartBehaviorTestRequest("test"), next, default);

        nextCalled.Should().BeTrue();
        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_FailingValidator_ShouldThrowValidationException()
    {
        var behavior = new ValidationBehavior<CartBehaviorTestRequest, string>([new CartAlwaysFailValidator()]);
        RequestHandlerDelegate<string> next = () => Task.FromResult("ok");

        var act = async () => await behavior.Handle(new CartBehaviorTestRequest("test"), next, default);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_MultipleValidatorsAllPass_ShouldCallNext()
    {
        var behavior = new ValidationBehavior<CartBehaviorTestRequest, string>(
        [new CartAlwaysPassValidator(), new CartAlwaysPassValidator()]);
        var nextCalled = false;
        RequestHandlerDelegate<string> next = () => { nextCalled = true; return Task.FromResult("ok"); };

        await behavior.Handle(new CartBehaviorTestRequest("test"), next, default);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_OneOfMultipleValidatorsFails_ShouldThrowValidationException()
    {
        var behavior = new ValidationBehavior<CartBehaviorTestRequest, string>(
        [new CartAlwaysPassValidator(), new CartAlwaysFailValidator()]);
        RequestHandlerDelegate<string> next = () => Task.FromResult("ok");

        var act = async () => await behavior.Handle(new CartBehaviorTestRequest("test"), next, default);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
