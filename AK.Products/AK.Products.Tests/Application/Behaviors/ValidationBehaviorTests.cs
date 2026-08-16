using AK.BuildingBlocks.Behaviors;
using FluentAssertions;
using FluentValidation;
using MediatR;

namespace AK.Products.Tests.Application.Behaviors;

public sealed record BehaviorTestRequest(string Value) : IRequest<string>;

public sealed class AlwaysPassValidator : AbstractValidator<BehaviorTestRequest> { }

public sealed class AlwaysFailValidator : AbstractValidator<BehaviorTestRequest>
{
    public AlwaysFailValidator() => RuleFor(x => x.Value).Must(_ => false).WithMessage("Always fails");
}

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_WithNoValidators_ShouldCallNext()
    {
        var nextCalled = false;
        RequestHandlerDelegate<string> next = () => { nextCalled = true; return Task.FromResult("result"); };
        var behavior = new ValidationBehavior<BehaviorTestRequest, string>([]);

        var result = await behavior.Handle(new BehaviorTestRequest("test"), next, default);

        result.Should().Be("result");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithValidRequest_ShouldCallNext()
    {
        var nextCalled = false;
        RequestHandlerDelegate<string> next = () => { nextCalled = true; return Task.FromResult("result"); };
        var behavior = new ValidationBehavior<BehaviorTestRequest, string>([new AlwaysPassValidator()]);

        var result = await behavior.Handle(new BehaviorTestRequest("test"), next, default);

        result.Should().Be("result");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithInvalidRequest_ShouldThrowValidationException()
    {
        var nextCalled = false;
        RequestHandlerDelegate<string> next = () => { nextCalled = true; return Task.FromResult(""); };
        var behavior = new ValidationBehavior<BehaviorTestRequest, string>([new AlwaysFailValidator()]);

        var act = () => behavior.Handle(new BehaviorTestRequest(""), next, default);

        await act.Should().ThrowAsync<ValidationException>();
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithMultipleValidatorsAllValid_ShouldCallNext()
    {
        RequestHandlerDelegate<string> next = () => Task.FromResult("ok");
        var behavior = new ValidationBehavior<BehaviorTestRequest, string>(
            [new AlwaysPassValidator(), new AlwaysPassValidator()]);

        var result = await behavior.Handle(new BehaviorTestRequest("test"), next, default);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_WithOneFailingValidator_ShouldThrowValidationException()
    {
        var nextCalled = false;
        RequestHandlerDelegate<string> next = () => { nextCalled = true; return Task.FromResult(""); };
        var behavior = new ValidationBehavior<BehaviorTestRequest, string>(
            [new AlwaysPassValidator(), new AlwaysFailValidator()]);

        var act = () => behavior.Handle(new BehaviorTestRequest("x"), next, default);

        await act.Should().ThrowAsync<ValidationException>();
        nextCalled.Should().BeFalse();
    }
}
