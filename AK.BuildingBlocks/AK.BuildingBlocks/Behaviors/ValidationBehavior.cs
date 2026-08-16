using FluentValidation;
using MediatR;

namespace AK.BuildingBlocks.Behaviors;

// MediatR pipeline behavior that runs before every command/query handler.
// When a command is sent via mediator.Send(command), MediatR builds a pipeline:
//   ValidationBehavior → actual command handler
//
// If any FluentValidation validator finds errors, this throws ValidationException
// which ExceptionHandlerMiddleware catches and returns as HTTP 400 Bad Request.
// The actual handler is never called if validation fails.
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        // No validator registered for this request type — skip straight to the handler.
        if (!validators.Any()) return await next();

        var context = new ValidationContext<TRequest>(request);

        // Run all validators and collect every failure across all of them.
        var failures = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count != 0)
            throw new ValidationException(failures);

        return await next();
    }
}
