using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Helm.Cpq.Api;

public static class CpqEndpoints
{
    public static IEndpointRouteBuilder MapCpqInfoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/cpq");

        group.MapGet("/_info", () => Results.Ok(new { module = "Cpq" }));

        return app;
    }
}
