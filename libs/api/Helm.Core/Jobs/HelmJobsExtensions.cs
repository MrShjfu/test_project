using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Helm.Core.Jobs;

/// <summary>
/// Wires Hangfire (ADR-005): PostgreSQL-backed job storage in the <c>hangfire</c> schema, the
/// Hangfire server, the admin-gated dashboard at <c>/hangfire</c>, and the daily outbox purge
/// recurring job. Mirrors the AddHelmAuth/UseHelmAuth pattern in Helm.Core.Auth.
/// </summary>
public static class HelmJobsExtensions
{
    public static IServiceCollection AddHelmJobs(this IServiceCollection services, IConfiguration config)
    {
        services.AddHangfire(cfg => cfg
            .UsePostgreSqlStorage(options => options
                .UseNpgsqlConnection(config.GetConnectionString("Helm")),
                new PostgreSqlStorageOptions { SchemaName = "hangfire" }));

        // Jobs:Enabled (default true) lets test hosts skip spinning up the Hangfire server —
        // avoids polling/thread overhead and startup races against a Testcontainers Postgres
        // that's about to be migrated. Hangfire storage/dashboard wiring above still registers
        // so DI resolves; only the background server thread is skipped.
        if (config.GetValue("Jobs:Enabled", true))
            services.AddHangfireServer();

        return services;
    }

    public static WebApplication UseHelmJobs(this WebApplication app)
    {
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = new[] { new HangfireAdminFilter() }
        });

        // Snapshot the current module list now, at startup, rather than passing a lazy
        // reference — Hangfire serializes the recurring job's captured method arguments (here,
        // the materialized List<string>) into hangfire.job, not a re-evaluated expression. New
        // module registrations after this point require an app restart to be picked up by the
        // recurring job; this is an accepted limitation, not a bug.
        var schemas = HelmModuleRegistry.Modules.Select(m => m.Schema).ToList();
        try
        {
            RecurringJob.AddOrUpdate<OutboxPurgeJob>(
                "outbox-purge",
                job => job.Run(schemas),
                Cron.Daily);
        }
        catch (Exception ex)
        {
            // Registration must never take the app down: AddOrUpdate is idempotent and every
            // replica runs it at startup, so one instance succeeding is enough. Under autoscale
            // (or after an unclean shutdown leaves a stale hangfire distributed-lock row),
            // replicas can time out contending for 'hangfire:lock:recurring-job:outbox-purge' —
            // crash-looping the whole app over a daily cleanup job would be a terrible trade.
            app.Logger.LogWarning(ex,
                "Recurring job 'outbox-purge' registration failed at startup; " +
                "another instance likely holds the lock or a stale lock exists. " +
                "Registration will be retried on next app start.");
        }

        return app;
    }
}
