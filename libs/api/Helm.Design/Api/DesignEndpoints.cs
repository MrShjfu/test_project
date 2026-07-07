using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Helm.Design.Api;

public static class DesignEndpoints
{
    public static IEndpointRouteBuilder MapDesignInfoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/design");

        group.MapGet("/_info", () => Results.Ok(new { module = "Design" }));

        return app;
    }
}
