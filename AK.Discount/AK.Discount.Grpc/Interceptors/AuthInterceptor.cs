using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace AK.Discount.Grpc.Interceptors;

public sealed class AuthInterceptor(ILogger<AuthInterceptor> logger) : Interceptor
{
    private static readonly HashSet<string> WriteMethodNames =
    [
        "/discount.DiscountProtoService/CreateDiscount",
        "/discount.DiscountProtoService/UpdateDiscount",
        "/discount.DiscountProtoService/DeleteDiscount"
    ];

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        if (WriteMethodNames.Contains(context.Method))
        {
            var authHeader = context.RequestHeaders.Get("authorization")?.Value;
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing or invalid authorization header."));

            var token = authHeader["Bearer ".Length..];
            if (!ValidateTokenAndHasAdminRole(token))
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Admin role required."));
        }

        return await continuation(request, context);
    }

    private bool ValidateTokenAndHasAdminRole(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);

            // KI-001 fix: Microsoft Entra emits app roles in a FLAT, top-level "roles" claim.
            // JwtSecurityTokenHandler expands that JSON array into one Claim per value, each with
            // Type == "roles" — so the admin check is a direct claim lookup. This matches how
            // AK.BuildingBlocks reads roles (RoleClaimType = "roles"), keeping authorization
            // consistent between the REST services and this gRPC service. (The previous provider
            // nested roles under realm_access.roles, which this interceptor used to parse.)
            return jwt.Claims.Any(c => c.Type == "roles" && c.Value == "admin");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Token validation failed");
            return false;
        }
    }
}
