using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Helm.Bff.Portal;

/// <summary>
/// Customer Portal BFF: empty authorized shell (ADR-008). No composition endpoints yet — the
/// route group and OpenAPI document exist so auth wiring and per-audience doc separation are in
/// place ahead of the first Portal feature.
/// </summary>
public static class PortalBff
{
    public const string OpenApiDocumentName = "portal";

    public static IServiceCollection AddPortalBff(this IServiceCollection services)
    {
        services.AddOpenApi(OpenApiDocumentName);
        return services;
    }

    public static IEndpointRouteBuilder MapPortalBff(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/bff/portal").WithGroupName(OpenApiDocumentName);
        return app;
    }
}
