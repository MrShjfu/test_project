using Helm.Core;
using Helm.Core.Outbox;
using Helm.Planning.Api;
using Helm.Planning.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Helm.Planning;

public static class PlanningModule
{
    public static IServiceCollection AddPlanningModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<PlanningDbContext>(o => o
            .UseNpgsql(config.GetConnectionString("Helm"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "planning")));

        services.AddHealthChecks().AddCheck<PlanningHealthCheck>("planning");
        services.AddHostedService<OutboxRelay<PlanningDbContext>>();

        HelmModuleRegistry.Register("Planning", "planning");

        return services;
    }

    public static IEndpointRouteBuilder MapPlanningEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPlanningInfoEndpoints();
        return app;
    }
}

/// <summary>
/// Cheap liveness check: can we open a raw connection to the database backing the planning schema.
/// Deliberately does not resolve <see cref="PlanningDbContext"/> — that requires an
/// <c>ICompanyContext</c>, which in turn requires an authenticated request's claims (see
/// AddHelmAuth), but health checks must also succeed for anonymous/unauthenticated callers.
/// </summary>
internal class PlanningHealthCheck(IConfiguration config) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(config.GetConnectionString("Helm"));
            await connection.OpenAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(exception: ex);
        }
    }
}
