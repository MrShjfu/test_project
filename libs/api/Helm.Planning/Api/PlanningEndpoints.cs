using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Helm.Planning.Api;

public static class PlanningEndpoints
{
    public static IEndpointRouteBuilder MapPlanningInfoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/planning");

        group.MapGet("/_info", () => Results.Ok(new { module = "Planning" }));

        return app;
    }
}
