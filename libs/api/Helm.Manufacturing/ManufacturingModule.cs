using Helm.Core;
using Helm.Core.Outbox;
using Helm.Manufacturing.Api;
using Helm.Manufacturing.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Helm.Manufacturing;

public static class ManufacturingModule
{
    public static IServiceCollection AddManufacturingModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<ManufacturingDbContext>(o => o
            .UseNpgsql(config.GetConnectionString("Helm"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "manufacturing")));

        services.AddHealthChecks().AddCheck<ManufacturingHealthCheck>("manufacturing");
        services.AddHostedService<OutboxRelay<ManufacturingDbContext>>();

        HelmModuleRegistry.Register("Manufacturing", "manufacturing");

        return services;
    }

    public static IEndpointRouteBuilder MapManufacturingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapManufacturingInfoEndpoints();
        return app;
    }
}

/// <summary>
/// Cheap liveness check: can we open a raw connection to the database backing the manufacturing schema.
/// Deliberately does not resolve <see cref="ManufacturingDbContext"/> — that requires an
/// <c>ICompanyContext</c>, which in turn requires an authenticated request's claims (see
/// AddHelmAuth), but health checks must also succeed for anonymous/unauthenticated callers.
/// </summary>
internal class ManufacturingHealthCheck(IConfiguration config) : IHealthCheck
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
