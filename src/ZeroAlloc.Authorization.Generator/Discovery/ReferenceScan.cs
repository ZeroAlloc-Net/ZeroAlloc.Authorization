using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Authorization.Generator.Discovery;

/// <summary>
/// The policies and requests declared in the referenced assemblies.
/// </summary>
/// <remarks>
/// <para>
/// <c>ForAttributeWithMetadataName</c> only sees this compilation's syntax trees, so a policy
/// shipped in a shared-kernel assembly needs a walk of the references. That walk visits every
/// type of every reference, so it runs from the metadata references alone and only when they
/// change, not on each edit.
/// </para>
/// <para>
/// The references are bound in a compilation of their own, with no syntax trees. It sees the
/// same assembly symbols the real compilation would, and none of its code, so an edit cannot
/// reach it. Its trees are empty, so every location it reports is <see cref="Location.None"/>,
/// which is how code outside this compilation must be reported anyway.
/// </para>
/// </remarks>
internal sealed record ReferenceScan(
    EquatableArray<PolicyDiscovery> Policies,
    EquatableArray<RequireDiscovery> Requires)
{
    private const string ScanAssemblyName = "ZeroAlloc.Authorization.Generator.ReferenceScan";

    public static ReferenceScan Run(ImmutableArray<MetadataReference> references, CancellationToken ct)
    {
        var compilation = CSharpCompilation.Create(ScanAssemblyName, references: references);
        return new ReferenceScan(
            PolicySymbolWalker.FindInReferences(compilation, ct),
            RequireSymbolWalker.FindInReferences(compilation, ct));
    }
}
