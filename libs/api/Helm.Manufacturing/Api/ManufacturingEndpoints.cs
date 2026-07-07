using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Helm.Manufacturing.Api;

public static class ManufacturingEndpoints
{
    public static IEndpointRouteBuilder MapManufacturingInfoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/manufacturing");

        group.MapGet("/_info", () => Results.Ok(new { module = "Manufacturing" }));

        return app;
    }
}
