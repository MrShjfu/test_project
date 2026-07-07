using Helm.Bff.Internal.Customers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Helm.Bff.Internal;

/// <summary>
/// Internal Platform BFF: composes reads across module Contracts for NTG-internal staff tooling
/// (ADR-008 — BFFs compose and shape only, business rules stay in modules). References
/// <c>Helm.Crm.Contracts</c> only, never <c>Helm.Crm</c> (enforced by
/// Helm.ArchTests.ModuleBoundaryTests.Bffs_reference_no_module_implementation).
/// </summary>
public static class InternalBff
{
    public const string OpenApiDocumentName = "internal";

    public static IServiceCollection AddInternalBff(this IServiceCollection services)
    {
        services.AddOpenApi(OpenApiDocumentName);
        return services;
    }

    public static IEndpointRouteBuilder MapInternalBff(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/bff/internal").WithGroupName(OpenApiDocumentName);

        group.MapCustomerListEndpoint();

        return app;
    }
}
