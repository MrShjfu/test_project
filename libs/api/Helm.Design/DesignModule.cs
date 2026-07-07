using Helm.Core;
using Helm.Core.Outbox;
using Helm.Design.Api;
using Helm.Design.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Helm.Design;

public static class DesignModule
{
    public static IServiceCollection AddDesignModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<DesignDbContext>(o => o
            .UseNpgsql(config.GetConnectionString("Helm"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "design")));

        services.AddHealthChecks().AddCheck<DesignHealthCheck>("design");
        services.AddHostedService<OutboxRelay<DesignDbContext>>();

        HelmModuleRegistry.Register("Design", "design");

        return services;
    }

    public static IEndpointRouteBuilder MapDesignEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapDesignInfoEndpoints();
        return app;
    }
}

/// <summary>
/// Cheap liveness check: can we open a raw connection to the database backing the design schema.
/// Deliberately does not resolve <see cref="DesignDbContext"/> — that requires an
/// <c>ICompanyContext</c>, which in turn requires an authenticated request's claims (see
/// AddHelmAuth), but health checks must also succeed for anonymous/unauthenticated callers.
/// </summary>
internal class DesignHealthCheck(IConfiguration config) : IHealthCheck
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
