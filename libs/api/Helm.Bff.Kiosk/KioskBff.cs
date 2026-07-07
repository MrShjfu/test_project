using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Helm.Bff.Kiosk;

/// <summary>
/// Factory Kiosk BFF: empty authorized shell (ADR-008). No composition endpoints yet — the route
/// group and OpenAPI document exist so auth wiring and per-audience doc separation are in place
/// ahead of the first Kiosk (offline PWA) feature.
/// </summary>
public static class KioskBff
{
    public const string OpenApiDocumentName = "kiosk";

    public static IServiceCollection AddKioskBff(this IServiceCollection services)
    {
        services.AddOpenApi(OpenApiDocumentName);
        return services;
    }

    public static IEndpointRouteBuilder MapKioskBff(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/bff/kiosk").WithGroupName(OpenApiDocumentName);
        return app;
    }
}
