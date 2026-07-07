using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Helm.PreProcessing.Api;

public static class PreProcessingEndpoints
{
    public static IEndpointRouteBuilder MapPreProcessingInfoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/pre_processing");

        group.MapGet("/_info", () => Results.Ok(new { module = "PreProcessing" }));

        return app;
    }
}
