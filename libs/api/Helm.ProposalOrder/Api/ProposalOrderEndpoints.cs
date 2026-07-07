using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Helm.ProposalOrder.Api;

public static class ProposalOrderEndpoints
{
    public static IEndpointRouteBuilder MapProposalOrderInfoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/proposal_order");

        group.MapGet("/_info", () => Results.Ok(new { module = "ProposalOrder" }));

        return app;
    }
}
