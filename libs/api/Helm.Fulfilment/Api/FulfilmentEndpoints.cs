using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Helm.Fulfilment.Api;

public static class FulfilmentEndpoints
{
    public static IEndpointRouteBuilder MapFulfilmentInfoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/fulfilment");

        group.MapGet("/_info", () => Results.Ok(new { module = "Fulfilment" }));

        return app;
    }
}
