using Asp.Versioning;
using Microsoft.Extensions.DependencyInjection;

namespace AK.BuildingBlocks.Versioning;

// Standardises API versioning across all services.
//
// Default version: 1.0, assumed when no version is specified (existing clients unaffected).
// Supported readers (clients can pick either):
//   - URL segment:  /api/v1/orders  (preferred — visible, cacheable)
//   - Header:       api-version: 1.0  (useful for clients that can't modify URLs)
//
// ReportApiVersions = true adds "api-supported-versions" response headers so clients
// can discover available versions without reading docs.
//
// When you need v2 of an endpoint in any service, create a parallel group:
//
//   var versionSet = app.NewApiVersionSet()
//       .HasApiVersion(new ApiVersion(1, 0))
//       .HasApiVersion(new ApiVersion(2, 0))
//       .Build();
//
//   var v1 = app.MapGroup("/api/v1/orders").WithApiVersionSet(versionSet).MapToApiVersion(1, 0);
//   var v2 = app.MapGroup("/api/v2/orders").WithApiVersionSet(versionSet).MapToApiVersion(2, 0);
//
// Both versions coexist — v1 clients are unaffected when v2 launches.
//
// Currently demonstrated in AK.Order. Other services adopt it by calling
// AddStandardApiVersioning() in their Program.cs — no other changes required.
public static class ApiVersioningExtensions
{
    public static IServiceCollection AddStandardApiVersioning(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
            options.ApiVersionReader = ApiVersionReader.Combine(
                new UrlSegmentApiVersionReader(),
                new HeaderApiVersionReader("api-version"));
        });

        return services;
    }
}
