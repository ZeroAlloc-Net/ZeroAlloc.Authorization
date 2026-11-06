using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ZeroAlloc.Authorization.Generator.Diagnostics;

namespace ZeroAlloc.Authorization.Generator.Discovery;

/// <summary>
/// Everything the generator found: this compilation's policies and requests, then those of its
/// references, each once.
/// </summary>
/// <param name="DeclaresOwn">Whether this compilation declares a policy or a request itself, as opposed to only inheriting them from its references.</param>
internal sealed record AuthorizationModel(
    EquatableArray<PolicyDiscovery> Policies,
    EquatableArray<RequireDiscovery> Requires,
    bool DeclaresOwn)
{
    public static AuthorizationModel Build(
        ImmutableArray<PolicyDiscovery> sourcePolicies,
        ImmutableArray<RequireDiscovery> sourceRequirePolicies,
        ImmutableArray<RequireDiscovery> sourceRequireAnyPolicies,
        ReferenceScan references)
    {
        // A partial type is found once per declaration that carries the attribute, and a type
        // with both [RequirePolicy] and [RequireAnyPolicy] by both providers. Each finding reads
        // all of the type's attributes, so the findings are alike and the first one stands.
        var policies = Distinct(sourcePolicies, static p => p.FullyQualifiedTypeName);

        // The two request providers each list their types in declaration order. Merged, the types
        // are ordered by file path and position, which is the same on every run. OrderBy is a
        // stable sort, so the two findings of one type keep their relative order.
        var requires = Distinct(
                sourceRequirePolicies.Concat(sourceRequireAnyPolicies).OrderBy(static r => r.TypeLocation, LocationInfo.Order),
                static r => r.FullyQualifiedTypeName);

        var declaresOwn = policies.Count > 0 || requires.Count > 0;
        policies.AddRange(references.Policies);
        requires.AddRange(references.Requires);
        return new AuthorizationModel(
            new EquatableArray<PolicyDiscovery>(policies.ToArray()),
            new EquatableArray<RequireDiscovery>(requires.ToArray()),
            declaresOwn);
    }

    private static List<T> Distinct<T>(IEnumerable<T> items, System.Func<T, string> key)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        var result = new List<T>();
        foreach (var item in items)
        {
            if (seen.Add(key(item))) result.Add(item);
        }
        return result;
    }
}
