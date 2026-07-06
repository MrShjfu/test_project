namespace Helm.Core;

/// <summary>
/// Process-wide registry of module name/schema pairs, populated by each module's own
/// <c>Add&lt;Mod&gt;Module</c> registration extension at startup. Consumed by
/// <see cref="Jobs.HelmJobsExtensions.UseHelmJobs"/> to know which schemas the outbox purge job
/// must sweep. Thread-safe; registration is idempotent so re-running module registration
/// (e.g. across multiple <c>WebApplicationFactory</c> host spins in the same test process)
/// does not produce duplicate entries.
/// </summary>
public static class HelmModuleRegistry
{
    private static readonly object Lock = new();
    private static readonly List<(string Name, string Schema)> ModuleList = [];

    public static void Register(string name, string schema)
    {
        lock (Lock)
        {
            if (ModuleList.Any(m => m.Name == name))
                return; // idempotent: ignore duplicate registration of the same module name

            ModuleList.Add((name, schema));
        }
    }

    public static IReadOnlyList<(string Name, string Schema)> Modules
    {
        get
        {
            lock (Lock)
            {
                return ModuleList.ToArray();
            }
        }
    }
}
