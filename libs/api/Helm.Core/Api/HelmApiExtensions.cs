using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

namespace Helm.Core.Api;

public static class HelmApiExtensions
{
    public static IServiceCollection AddHelmApi(this IServiceCollection services)
    {
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);
        services.AddHealthChecks();
        return services;
    }

    public static WebApplication UseHelmApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.MapHealthChecks("/health").AllowAnonymous();
        return app;
    }
}
