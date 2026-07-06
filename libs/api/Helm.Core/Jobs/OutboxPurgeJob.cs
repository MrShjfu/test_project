using Hangfire;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Helm.Core.Jobs;

/// <summary>
/// Recurring Hangfire job (see <see cref="HelmJobsExtensions.UseHelmJobs"/>) that purges processed
/// outbox rows and processed-event markers older than 30 days, per module schema (ADR-004 "Cleanup").
/// </summary>
[AutomaticRetry(Attempts = 3)]
public class OutboxPurgeJob(IConfiguration configuration)
{
    public async Task Run(IEnumerable<string> schemas)
    {
        var connectionString = configuration.GetConnectionString("Helm");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        foreach (var schema in schemas)
        {
            // Schema names are never user input — they come exclusively from
            // HelmModuleRegistry, which is populated only by our own module registration code
            // at startup (see Add<Mod>Module extensions). SQL does not support parameterizing
            // identifiers (schema/table names) via command parameters, so string-interpolating
            // a trusted, internally-sourced schema name here is acceptable and is the only way
            // to target a dynamic schema.
            await using var outboxCmd = new NpgsqlCommand(
                $"DELETE FROM {schema}.outbox WHERE processed_at < now() - interval '30 days'",
                connection);
            // SQL NULL semantics note: `NULL < now() - interval '30 days'` evaluates to NULL, not
            // TRUE, so rows with processed_at IS NULL (never processed) are never matched by this
            // WHERE clause. An explicit `processed_at IS NOT NULL AND ...` guard is unnecessary —
            // do not "fix" this by adding one; it is already correct per SQL NULL comparison rules.
            await outboxCmd.ExecuteNonQueryAsync();

            await using var processedEventsCmd = new NpgsqlCommand(
                $"DELETE FROM {schema}.processed_events WHERE processed_at < now() - interval '30 days'",
                connection);
            await processedEventsCmd.ExecuteNonQueryAsync();
        }
    }
}
