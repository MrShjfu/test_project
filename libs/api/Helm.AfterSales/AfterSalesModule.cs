using Helm.Core;
using Helm.Core.Outbox;
using Helm.AfterSales.Api;
using Helm.AfterSales.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Helm.AfterSales;

public static class AfterSalesModule
{
    public static IServiceCollection AddAfterSalesModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AfterSalesDbContext>(o => o
            .UseNpgsql(config.GetConnectionString("Helm"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "after_sales")));

        services.AddHealthChecks().AddCheck<AfterSalesHealthCheck>("after_sales");
        services.AddHostedService<OutboxRelay<AfterSalesDbContext>>();

        HelmModuleRegistry.Register("AfterSales", "after_sales");

        return services;
    }

    public static IEndpointRouteBuilder MapAfterSalesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAfterSalesInfoEndpoints();
        return app;
    }
}

/// <summary>
/// Cheap liveness check: can we open a raw connection to the database backing the after_sales schema.
/// Deliberately does not resolve <see cref="AfterSalesDbContext"/> — that requires an
/// <c>ICompanyContext</c>, which in turn requires an authenticated request's claims (see
/// AddHelmAuth), but health checks must also succeed for anonymous/unauthenticated callers.
/// </summary>
internal class AfterSalesHealthCheck(IConfiguration config) : IHealthCheck
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
