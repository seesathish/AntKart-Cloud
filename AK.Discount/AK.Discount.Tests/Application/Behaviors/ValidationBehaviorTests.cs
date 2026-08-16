using AK.BuildingBlocks.Behaviors;
using FluentAssertions;
using FluentValidation;
using MediatR;

namespace AK.Discount.Tests.Application.Behaviors;

public sealed record DiscountBehaviorTestRequest(string Value) : IRequest<string>;

public sealed class DiscountAlwaysPassValidator : AbstractValidator<DiscountBehaviorTestRequest> { }

public sealed class DiscountAlwaysFailValidator : AbstractValidator<DiscountBehaviorTestRequest>
{
    public DiscountAlwaysFailValidator() => RuleFor(x => x.Value).Must(_ => false).WithMessage("Always fails");
}

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_WithNoValidators_ShouldCallNext()
    {
        var nextCalled = false;
        RequestHandlerDelegate<string> next = () => { nextCalled = true; return Task.FromResult("result"); };
        var behavior = new ValidationBehavior<DiscountBehaviorTestRequest, string>([]);

        var result = await behavior.Handle(new DiscountBehaviorTestRequest("test"), next, default);

        result.Should().Be("result");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithValidRequest_ShouldCallNext()
    {
        var nextCalled = false;
        RequestHandlerDelegate<string> next = () => { nextCalled = true; return Task.FromResult("result"); };
        var behavior = new ValidationBehavior<DiscountBehaviorTestRequest, string>(
            [new DiscountAlwaysPassValidator()]);

        var result = await behavior.Handle(new DiscountBehaviorTestRequest("test"), next, default);

        result.Should().Be("result");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithInvalidRequest_ShouldThrowValidationException()
    {
        var nextCalled = false;
        RequestHandlerDelegate<string> next = () => { nextCalled = true; return Task.FromResult(""); };
        var behavior = new ValidationBehavior<DiscountBehaviorTestRequest, string>(
            [new DiscountAlwaysFailValidator()]);

        var act = () => behavior.Handle(new DiscountBehaviorTestRequest(""), next, default);

        await act.Should().ThrowAsync<ValidationException>();
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithMultipleValidatorsAllValid_ShouldCallNext()
    {
        RequestHandlerDelegate<string> next = () => Task.FromResult("ok");
        var behavior = new ValidationBehavior<DiscountBehaviorTestRequest, string>(
            [new DiscountAlwaysPassValidator(), new DiscountAlwaysPassValidator()]);

        var result = await behavior.Handle(new DiscountBehaviorTestRequest("test"), next, default);

        result.Should().Be("ok");
    }
}
