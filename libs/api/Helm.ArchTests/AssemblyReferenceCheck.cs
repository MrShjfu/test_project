using Mono.Cecil;

namespace Helm.ArchTests;

/// <summary>
/// Exact "does assembly A reference assembly B" check via Mono.Cecil's assembly-reference table.
/// </summary>
///
/// <remarks>
/// NetArchTest's own <c>NotHaveDependencyOnAny</c> matches by namespace prefix on a tree walk
/// (see NetArchTest.Rules.Dependencies.DataStructures.NamespaceTree — it explicitly documents
/// matching "the same results ... as String.StartsWith"). That is exactly right for
/// "module implementation may only depend on other modules' Contracts", where module names never
/// nest inside one another. It breaks for any check involving a module's own Contracts, because
/// <c>Helm.Crm.Contracts.*</c> is a namespace *descendant* of <c>Helm.Crm</c>, not a sibling — a
/// search for dependencies on <c>"Helm.Crm"</c> matches <c>Helm.Crm.Contracts.ICrmService</c>
/// even though Contracts obviously does not depend on its own impl assembly. Reproduced and
/// confirmed against NetArchTest.Rules 1.3.2 while building this suite (see task-12-report.md).
///
/// This type sidesteps the namespace tree entirely and reads the physical assembly-reference
/// table instead — exact name match, no ancestor/descendant ambiguity.
/// </remarks>
public static class AssemblyReferenceCheck
{
    /// <summary>True if <paramref name="assembly"/>'s assembly-reference table names <paramref name="referencedAssemblyName"/> (exact, e.g. "Helm.Crm").</summary>
    public static bool References(System.Reflection.Assembly assembly, string referencedAssemblyName)
    {
        using var definition = AssemblyDefinition.ReadAssembly(assembly.Location);
        return definition.MainModule.AssemblyReferences
            .Any(r => string.Equals(r.Name, referencedAssemblyName, StringComparison.Ordinal));
    }
}
