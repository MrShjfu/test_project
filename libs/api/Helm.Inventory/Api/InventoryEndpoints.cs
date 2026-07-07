using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Helm.Inventory.Api;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryInfoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/inventory");

        group.MapGet("/_info", () => Results.Ok(new { module = "Inventory" }));

        return app;
    }
}
