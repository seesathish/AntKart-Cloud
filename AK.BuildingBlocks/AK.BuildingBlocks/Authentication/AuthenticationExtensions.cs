using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace AK.BuildingBlocks.Authentication;

// Shared JWT authentication used by every service (Products, Cart, Order, Payments,
// Notification, Gateway). Calling AddEntraAuthentication() from a service's Program.cs
// wires up token validation against Microsoft Entra ID once — no per-service copy.
//
// AUTHENTICATION (is this a genuine, current token from the right issuer for this API?) is
// established by the four checks below. AUTHORIZATION (what may the caller do?) is then driven
// by the FLAT "roles" claim Entra emits — the same claim every service reads, which keeps role
// checks consistent platform-wide.
public static class AuthenticationExtensions
{
    public static IServiceCollection AddEntraAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = configuration.GetSection("Entra").Get<EntraSettings>()
            ?? throw new InvalidOperationException("Entra settings are missing from configuration.");

        services.Configure<EntraSettings>(configuration.GetSection("Entra"));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Authority is the Entra v2 OIDC endpoint for the tenant. The middleware downloads
                // {Authority}/.well-known/openid-configuration and the JWKS signing keys from it
                // automatically — and refreshes them as Entra rotates keys — so signature
                // validation never needs a stored key or secret.
                options.Authority = settings.ResolveAuthority();
                options.RequireHttpsMetadata = settings.RequireHttpsMetadata;

                // Keep the original JWT claim names (e.g. "sub", "roles", "email") instead of
                // remapping them to long XML-style URIs, so downstream code and the role check
                // below read the claims exactly as Entra issues them.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // 1. Issuer — the token must come from OUR tenant's Entra v2 issuer.
                    ValidateIssuer = true,
                    ValidIssuer = settings.ResolveIssuer(),

                    // 2. Audience — the token must be intended for THIS API's app registration.
                    //    Its `aud` may take EITHER valid form, so BOTH are accepted:
                    //      • the App ID URI (api://<name>) — when a SEPARATE client app requests a
                    //        token for this API;
                    //      • the client-id GUID — when the client and the resource are the SAME app
                    //        (the API requesting a token for itself).
                    //    Accepting both still stops a token minted for ANOTHER API being replayed
                    //    here (neither of its audiences would match). Empty values are filtered out.
                    ValidateAudience = true,
                    ValidAudiences = settings.ResolveValidAudiences(),

                    // 3. Lifetime — not expired and not used before its valid-from time.
                    ValidateLifetime = true,

                    // 4. Signature — signed by one of Entra's published signing keys.
                    ValidateIssuerSigningKey = true,

                    // Authorization source: Entra emits app roles in a FLAT top-level "roles"
                    // claim (a JSON array of role names such as admin and user).
                    // Mapping RoleClaimType to "roles" makes [Authorize(Roles=...)],
                    // RequireRole("admin") and the policies below read that claim directly — with
                    // no parsing of a nested provider-specific structure. (The previous provider
                    // nested roles under realm_access.roles, which each service had to unpack;
                    // consuming the flat claim is what makes role checks consistent everywhere,
                    // and is what resolves the gRPC interceptor mismatch tracked as KI-001.)
                    RoleClaimType = "roles"
                };
            });

        // Named policies used by endpoints via .RequireAuthorization("admin") / "authenticated".
        services.AddAuthorization(options =>
        {
            // "admin" policy: the token's flat "roles" claim must contain "admin".
            options.AddPolicy("admin", policy => policy.RequireRole("admin"));

            // "authenticated" policy: any valid Entra token, regardless of roles.
            options.AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());
        });

        return services;
    }

    // Registers UseAuthentication() then UseAuthorization() in the correct order.
    // Authentication must precede Authorization — reversed, policies always fail because the
    // user identity has not been established yet.
    public static WebApplication UseEntraAuth(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
