using System;
using System.Collections.Generic;
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

    public static ReferenceScan Run(ImmutableArray<MetadataReference> references, ScanTarget target, CancellationToken ct)
    {
        // The scan compilation takes the identity of the real one, so that a reference which
        // makes its internals visible to the real assembly makes them visible here too.
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        if (target.PublicKey.Length > 0)
        {
            options = options.WithCryptoPublicKey(ImmutableArray.Create(Convert.FromBase64String(target.PublicKey)));
        }
        var compilation = CSharpCompilation.Create(target.AssemblyName, references: references, options: options);

        var inaccessibleNames = new HashSet<string>(StringComparer.Ordinal);
        var policies = PolicySymbolWalker.FindInReferences(compilation, inaccessibleNames, ct);
        var requires = RequireSymbolWalker.FindInReferences(compilation, ct);

        // A request that needs a policy the compilation cannot use has no valid authorizer here.
        // The assembly that declares both registers it itself, so it is left out, not diagnosed.
        if (inaccessibleNames.Count > 0)
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in policies)
            {
                if (p.Policy is { } policy) known.Add(policy.PolicyName);
            }
            var kept = new List<RequireDiscovery>(requires.Count);
            foreach (var r in requires)
            {
                if (!NeedsInaccessible(r, inaccessibleNames, known)) kept.Add(r);
            }
            requires = new EquatableArray<RequireDiscovery>(kept.ToArray());
        }
        return new ReferenceScan(policies, requires);
    }

    private static bool NeedsInaccessible(RequireDiscovery request, HashSet<string> inaccessible, HashSet<string> known)
    {
        if (request.Require is not { } require) return false;
        foreach (var group in require.Groups)
        {
            foreach (var name in group.PolicyNames)
            {
                if (inaccessible.Contains(name) && !known.Contains(name)) return true;
            }
        }
        return false;
    }
}
