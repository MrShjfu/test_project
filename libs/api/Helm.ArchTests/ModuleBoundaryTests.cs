using FluentAssertions;

namespace Helm.ArchTests;

/// <summary>
/// Build-breaking enforcement of ADR-001/002 module isolation: a module implementation may
/// depend only on other modules' *.Contracts, never their implementation; Contracts must not
/// depend on any implementation (including their own module's); BFFs (Task 16+) must not depend
/// on any implementation either. Modules are discovered by <see cref="ModuleDiscovery"/> — see
/// that type for why discovery is assembly-based rather than driven by
/// <c>HelmModuleRegistry</c>.
/// </summary>
///
/// <remarks>
/// All checks here use <see cref="AssemblyReferenceCheck"/> (exact assembly-reference-table
/// lookup via Mono.Cecil) rather than NetArchTest's <c>Should().NotHaveDependencyOnAny(...)</c>.
/// NetArchTest's dependency search matches by namespace-prefix tree walk, which is correct for
/// "does module A depend on module B" (sibling module names never nest) but breaks for anything
/// involving a module's own Contracts: <c>Helm.Crm.Contracts.*</c> is a namespace *descendant* of
/// <c>Helm.Crm</c>, not a sibling, so a namespace search for dependencies on <c>"Helm.Crm"</c>
/// flags <c>Helm.Crm.Contracts.ICrmService</c> as if Contracts depended on its own impl. This was
/// reproduced against NetArchTest.Rules 1.3.2 while building this suite — see
/// task-12-report.md for the failing output. Exact assembly-reference matching has no such
/// ambiguity, so it is used uniformly below instead of switching mechanisms per-rule.
/// </remarks>
public class ModuleBoundaryTests
{
    private static readonly string[] BffNames = ["Internal", "Portal", "Kiosk"];

    public static IEnumerable<object[]> Modules => ModuleDiscovery.ModuleNames.Select(m => new object[] { m });

    /// <summary>
    /// Guards against <see cref="ModuleDiscovery"/> silently discovering zero modules (e.g. a
    /// naming-convention change, a discovery bug, or the Host project reference going stale) —
    /// which would make every [Theory] below vacuously pass with an empty data set and give false
    /// confidence that the boundary suite is doing something. Crm exists from Task inception, so
    /// its absence here means discovery itself is broken, not that Crm was deleted.
    /// </summary>
    [Fact]
    public void Discovery_finds_the_known_Crm_module()
    {
        ModuleDiscovery.ModuleNames.Should().Contain("Crm",
            "module discovery must find at least the Crm module; an empty ModuleNames would make " +
            "every boundary [Theory] in this suite vacuously pass without testing anything");
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_does_not_reference_other_module_implementations(string module)
    {
        var assembly = ModuleDiscovery.Assembly(module);

        foreach (var other in ModuleDiscovery.ModuleNames.Where(m => m != module))
        {
            AssemblyReferenceCheck.References(assembly, $"Helm.{other}").Should().BeFalse(
                $"Helm.{module} must depend on Helm.{other}.Contracts only, never its implementation (ADR-001/002)");
        }
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Contracts_do_not_reference_any_implementation(string module)
    {
        var assembly = ModuleDiscovery.Assembly($"{module}.Contracts");

        foreach (var impl in ModuleDiscovery.ModuleNames)
        {
            AssemblyReferenceCheck.References(assembly, $"Helm.{impl}").Should().BeFalse(
                $"Helm.{module}.Contracts must not depend on any module implementation, including its own (ADR-001/002)");
        }
    }

    /// <summary>
    /// Skips (rather than fails) per-BFF until Task 16 creates Helm.Bff.Internal/Portal/Kiosk —
    /// TryAssembly means "no BFF projects yet" is not conflated with "a BFF violated the rule".
    /// Once BFFs exist this exercises the real ADR-008 boundary (BFFs compose and shape only).
    /// </summary>
    [Fact]
    public void Bffs_reference_no_module_implementation()
    {
        foreach (var bff in BffNames)
        {
            if (!ModuleDiscovery.TryAssembly($"Bff.{bff}", out var assembly))
                continue;

            foreach (var impl in ModuleDiscovery.ModuleNames)
            {
                AssemblyReferenceCheck.References(assembly, $"Helm.{impl}").Should().BeFalse(
                    $"Helm.Bff.{bff} must depend on Helm.{impl}.Contracts only, never its implementation (ADR-008)");
            }
        }
    }

    /// <summary>
    /// Explicit "only Helm.Host may reference implementations" check. Largely implied by the
    /// tests above (every module, Contracts, and BFF assembly is already forbidden from
    /// depending on any impl), but those only cover module/Contracts/BFF assemblies as the
    /// *dependent* side. This test instead walks each impl assembly as the *dependency* side and
    /// confirms nothing outside Host consumes it — cheap given ModuleNames is already computed,
    /// and it directly encodes the composition-root rule rather than leaving it as an inference.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modules))]
    public void Only_Host_references_module_implementation(string module)
    {
        var dependents = ModuleDiscovery.ModuleNames
            .Where(m => m != module)
            .Select(ModuleDiscovery.Assembly)
            .Concat(ModuleDiscovery.ModuleNames.Select(m => ModuleDiscovery.Assembly($"{m}.Contracts")))
            .Concat(BffNames.Where(b => ModuleDiscovery.TryAssembly($"Bff.{b}", out _))
                .Select(b => ModuleDiscovery.Assembly($"Bff.{b}")));

        foreach (var dependent in dependents)
        {
            AssemblyReferenceCheck.References(dependent, $"Helm.{module}").Should().BeFalse(
                $"{dependent.GetName().Name} must not depend on Helm.{module} implementation; only Helm.Host may");
        }
    }
}
