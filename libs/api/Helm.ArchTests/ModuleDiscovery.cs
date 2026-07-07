using System.Reflection;

namespace Helm.ArchTests;

/// <summary>
/// Discovers Helm domain modules by reflection over loaded/loadable <c>Helm.*</c> assemblies —
/// deliberately independent of <see cref="Helm.Core.HelmModuleRegistry"/>, which only knows about
/// modules that have actually called <c>Add&lt;Mod&gt;Module</c> at runtime. ArchTests must catch
/// boundary violations even in a module that forgot to register itself, so this type does its own
/// assembly-based discovery instead of trusting the registry.
///
/// A "module" is a pair of assemblies named <c>Helm.&lt;Name&gt;</c> and
/// <c>Helm.&lt;Name&gt;.Contracts</c>. Infra assemblies (<c>Helm.Core</c>, <c>Helm.Host</c>,
/// <c>Helm.ArchTests</c>, <c>Helm.Bff.*</c>) and any <c>*.Tests</c> assembly are excluded even
/// though they match the <c>Helm.*</c> naming convention.
/// </summary>
public static class ModuleDiscovery
{
    private static readonly string[] InfraNames = ["Core", "Host", "ArchTests"];

    private static readonly Lazy<IReadOnlyDictionary<string, Assembly>> AllHelmAssemblies = new(LoadAllHelmAssemblies);

    private static readonly Lazy<IReadOnlyList<string>> Modules = new(DiscoverModuleNames);

    /// <summary>Domain module names, e.g. "Crm" — NOT prefixed with "Helm." and NOT including ".Contracts".</summary>
    public static IReadOnlyList<string> ModuleNames => Modules.Value;

    /// <summary>
    /// Resolves an assembly by its suffix after "Helm.", e.g. "Crm" -&gt; Helm.Crm,
    /// "Crm.Contracts" -&gt; Helm.Crm.Contracts, "Bff.Internal" -&gt; Helm.Bff.Internal.
    /// Throws if not found — use <see cref="TryAssembly"/> for assemblies that may not exist yet
    /// (e.g. BFFs before Task 16).
    /// </summary>
    public static Assembly Assembly(string suffix)
    {
        if (TryAssembly(suffix, out var assembly))
            return assembly;

        throw new InvalidOperationException(
            $"No loaded assembly named 'Helm.{suffix}'. Loaded Helm.* assemblies: " +
            string.Join(", ", AllHelmAssemblies.Value.Keys.OrderBy(k => k)));
    }

    public static bool TryAssembly(string suffix, out Assembly assembly) =>
        AllHelmAssemblies.Value.TryGetValue($"Helm.{suffix}", out assembly!);

    /// <summary>
    /// Force-loads every "Helm.*.dll" sitting next to this test assembly. A plain
    /// AppDomain/assembly-load-context walk (e.g. AppDomain.CurrentDomain.GetAssemblies()) only
    /// sees assemblies the runtime has already loaded for some other reason (satisfying a static
    /// reference, JIT-ing a call, etc). Modules that exist as build outputs but happen not to be
    /// exercised by any code path this process runs would then be silently invisible to
    /// discovery — which is exactly the failure mode this suite exists to prevent. Loading every
    /// Helm.*.dll from the output directory up front guarantees completeness regardless of what
    /// the rest of the test process happens to touch.
    /// </summary>
    private static IReadOnlyDictionary<string, Assembly> LoadAllHelmAssemblies()
    {
        var loaded = new Dictionary<string, Assembly>(StringComparer.Ordinal);

        void Track(Assembly a)
        {
            var name = a.GetName().Name;
            if (name is not null && name.StartsWith("Helm.", StringComparison.Ordinal))
                loaded[name] = a;
        }

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            Track(a);

        var baseDir = AppContext.BaseDirectory;
        foreach (var dll in Directory.EnumerateFiles(baseDir, "Helm.*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(dll);
            if (loaded.ContainsKey(name))
                continue;

            try
            {
                Track(System.Reflection.Assembly.LoadFrom(dll));
            }
            catch (BadImageFormatException)
            {
                // Not a managed assembly (shouldn't happen for Helm.*.dll, but don't let a
                // stray native/mixed binary abort discovery).
            }
        }

        return loaded;
    }

    private static IReadOnlyList<string> DiscoverModuleNames()
    {
        var assemblies = AllHelmAssemblies.Value;

        return assemblies.Keys
            .Where(name => name.StartsWith("Helm.", StringComparison.Ordinal))
            .Select(name => name["Helm.".Length..])
            .Where(IsCandidateModuleImplName)
            .Where(name => assemblies.ContainsKey($"Helm.{name}.Contracts"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// True for a bare "Helm.&lt;Name&gt;" assembly name that could be a module implementation:
    /// no further dots (so it isn't itself a ".Contracts"/".Tests" satellite), not an infra name,
    /// and not a BFF (BFFs are composition roots like modules' Host, not domain modules).
    /// </summary>
    private static bool IsCandidateModuleImplName(string name) =>
        !name.Contains('.', StringComparison.Ordinal) &&
        !InfraNames.Contains(name, StringComparer.Ordinal) &&
        !name.EndsWith("Tests", StringComparison.Ordinal) &&
        name != "Bff";
}
