using Helm.Core;
using Helm.Core.Outbox;
using Helm.PreProcessing.Api;
using Helm.PreProcessing.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Helm.PreProcessing;

public static class PreProcessingModule
{
    public static IServiceCollection AddPreProcessingModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<PreProcessingDbContext>(o => o
            .UseNpgsql(config.GetConnectionString("Helm"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "pre_processing")));

        services.AddHealthChecks().AddCheck<PreProcessingHealthCheck>("pre_processing");
        services.AddHostedService<OutboxRelay<PreProcessingDbContext>>();

        HelmModuleRegistry.Register("PreProcessing", "pre_processing");

        return services;
    }

    public static IEndpointRouteBuilder MapPreProcessingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPreProcessingInfoEndpoints();
        return app;
    }
}

/// <summary>
/// Cheap liveness check: can we open a raw connection to the database backing the pre_processing schema.
/// Deliberately does not resolve <see cref="PreProcessingDbContext"/> — that requires an
/// <c>ICompanyContext</c>, which in turn requires an authenticated request's claims (see
/// AddHelmAuth), but health checks must also succeed for anonymous/unauthenticated callers.
/// </summary>
internal class PreProcessingHealthCheck(IConfiguration config) : IHealthCheck
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
