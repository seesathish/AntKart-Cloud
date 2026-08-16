using FluentValidation;
using Grpc.Core;
using Grpc.Core.Interceptors;
namespace AK.Discount.Grpc.Interceptors;
public class ExceptionInterceptor(ILogger<ExceptionInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try { return await continuation(request, context); }
        catch (ValidationException ex)
        {
            var errors = string.Join("; ", ex.Errors.Select(e => e.ErrorMessage));
            logger.LogWarning("Validation failed: {Errors}", errors);
            throw new RpcException(new Status(StatusCode.InvalidArgument, errors));
        }
        catch (KeyNotFoundException ex)
        {
            logger.LogWarning("Not found: {Message}", ex.Message);
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Conflict: {Message}", ex.Message);
            throw new RpcException(new Status(StatusCode.AlreadyExists, ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            throw new RpcException(new Status(StatusCode.Internal, "An internal error occurred."));
        }
    }
}
