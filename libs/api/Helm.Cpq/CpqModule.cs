using Helm.Core;
using Helm.Core.Outbox;
using Helm.Cpq.Api;
using Helm.Cpq.Application;
using Helm.Cpq.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Helm.Cpq;

public static class CpqModule
{
    public static IServiceCollection AddCpqModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<CpqDbContext>(o => o
            .UseNpgsql(config.GetConnectionString("Helm"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "cpq")));

        services.AddHealthChecks().AddCheck<CpqHealthCheck>("cpq");
        services.AddHostedService<OutboxRelay<CpqDbContext>>();
        services.AddHostedService<CustomerCreatedConsumer>();

        HelmModuleRegistry.Register("Cpq", "cpq");

        return services;
    }

    public static IEndpointRouteBuilder MapCpqEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapCpqInfoEndpoints();
        return app;
    }
}

/// <summary>
/// Cheap liveness check: can we open a raw connection to the database backing the cpq schema.
/// Deliberately does not resolve <see cref="CpqDbContext"/> — that requires an
/// <c>ICompanyContext</c>, which in turn requires an authenticated request's claims (see
/// AddHelmAuth), but health checks must also succeed for anonymous/unauthenticated callers.
/// </summary>
internal class CpqHealthCheck(IConfiguration config) : IHealthCheck
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
