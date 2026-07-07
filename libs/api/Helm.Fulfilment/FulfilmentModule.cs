using Helm.Core;
using Helm.Core.Outbox;
using Helm.Fulfilment.Api;
using Helm.Fulfilment.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Helm.Fulfilment;

public static class FulfilmentModule
{
    public static IServiceCollection AddFulfilmentModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<FulfilmentDbContext>(o => o
            .UseNpgsql(config.GetConnectionString("Helm"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "fulfilment")));

        services.AddHealthChecks().AddCheck<FulfilmentHealthCheck>("fulfilment");
        services.AddHostedService<OutboxRelay<FulfilmentDbContext>>();

        HelmModuleRegistry.Register("Fulfilment", "fulfilment");

        return services;
    }

    public static IEndpointRouteBuilder MapFulfilmentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapFulfilmentInfoEndpoints();
        return app;
    }
}

/// <summary>
/// Cheap liveness check: can we open a raw connection to the database backing the fulfilment schema.
/// Deliberately does not resolve <see cref="FulfilmentDbContext"/> — that requires an
/// <c>ICompanyContext</c>, which in turn requires an authenticated request's claims (see
/// AddHelmAuth), but health checks must also succeed for anonymous/unauthenticated callers.
/// </summary>
internal class FulfilmentHealthCheck(IConfiguration config) : IHealthCheck
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
