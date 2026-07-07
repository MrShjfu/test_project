using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Helm.AfterSales.Api;

public static class AfterSalesEndpoints
{
    public static IEndpointRouteBuilder MapAfterSalesInfoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/after_sales");

        group.MapGet("/_info", () => Results.Ok(new { module = "AfterSales" }));

        return app;
    }
}
