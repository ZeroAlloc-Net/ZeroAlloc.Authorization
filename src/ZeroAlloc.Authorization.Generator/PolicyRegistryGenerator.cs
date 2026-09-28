using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ZeroAlloc.Authorization.Generator.Diagnostics;
using ZeroAlloc.Authorization.Generator.Discovery;
using ZeroAlloc.Authorization.Generator.Emit;

namespace ZeroAlloc.Authorization.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class PolicyRegistryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var compilationProvider = context.CompilationProvider;
        context.RegisterSourceOutput(compilationProvider, GenerateForCompilation);
    }

    private static void GenerateForCompilation(SourceProductionContext spc, Compilation compilation)
    {
        var policyResult = PolicySymbolWalker.Find(compilation);
        var policies = policyResult.Policies;
        ReportAll(spc, policyResult.Diagnostics);

        var requireResult = RequireSymbolWalker.Find(compilation);
        var requires = requireResult.Requires;
        ReportAll(spc, requireResult.Diagnostics);

        if (policies.Count == 0 && requires.Count == 0) return;

        ReportDuplicatePolicyNames(spc, policies);
        var byName = BuildPolicyByNameMap(policies);
        ReportUnknownPolicyReferences(spc, requires, byName);

        var emitDiagnostics = new List<Diagnostic>();
        var authorizers = AuthorizerForEmitter.Emit(requires, byName, emitDiagnostics);
        ReportAll(spc, emitDiagnostics);
        var registration = DIRegistrationEmitter.Emit(policies, requires);

        spc.AddSource("ZeroAllocAuthorization.Generated.g.cs", SourceText.From(authorizers + registration, System.Text.Encoding.UTF8));
    }

    private static void ReportAll(SourceProductionContext spc, IReadOnlyList<Diagnostic> diagnostics)
    {
        for (int i = 0; i < diagnostics.Count; i++)
        {
            spc.ReportDiagnostic(diagnostics[i]);
        }
    }

    // ZAUTH002: detect duplicate [Policy] names before building byName. Reported once per name, at
    // the later [Policy] attribute in this compilation, with the others here as additional
    // locations. A clash between referenced assemblies only has no location in this compilation.
    private static void ReportDuplicatePolicyNames(SourceProductionContext spc, IReadOnlyList<PolicyInfo> policies)
    {
        var byName = new Dictionary<string, List<PolicyInfo>>(System.StringComparer.Ordinal);
        var names = new List<string>();
        for (int i = 0; i < policies.Count; i++)
        {
            var name = policies[i].PolicyName;
            if (!byName.TryGetValue(name, out var same))
            {
                same = new List<PolicyInfo>();
                byName.Add(name, same);
                names.Add(name);
            }
            same.Add(policies[i]);
        }
        foreach (var name in names)
        {
            var same = byName[name];
            if (same.Count < 2) continue;

            var located = new List<Location>(same.Count);
            foreach (var policy in same)
            {
                if (policy.AttributeLocation.IsInSource) located.Add(policy.AttributeLocation);
            }
            located.Sort(SourceLocations.Compare);

            var primary = Location.None;
            if (located.Count > 0)
            {
                primary = located[located.Count - 1];
                located.RemoveAt(located.Count - 1);
            }
            spc.ReportDiagnostic(Diagnostic.Create(Descriptors.DuplicatePolicyName, primary, located, name));
        }
    }

    // Build a name→info dictionary for the emitter (last-write-wins for duplicates; ZAUTH002 flags them).
    private static Dictionary<string, PolicyInfo> BuildPolicyByNameMap(IReadOnlyList<PolicyInfo> policies)
    {
        var byName = new Dictionary<string, PolicyInfo>(System.StringComparer.Ordinal);
        for (int i = 0; i < policies.Count; i++)
        {
            byName[policies[i].PolicyName] = policies[i];
        }
        return byName;
    }

    // ZAUTH001: every [RequirePolicy] name must resolve to a known [Policy]. At the attribute that
    // names it.
    private static void ReportUnknownPolicyReferences(
        SourceProductionContext spc,
        IReadOnlyList<RequireInfo> requires,
        IReadOnlyDictionary<string, PolicyInfo> byName)
    {
        for (int i = 0; i < requires.Count; i++)
        {
            var req = requires[i];
            for (int g = 0; g < req.Groups.Count; g++)
            {
                var group = req.Groups[g];
                for (int j = 0; j < group.PolicyNames.Count; j++)
                {
                    var name = group.PolicyNames[j];
                    if (!byName.ContainsKey(name))
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(Descriptors.UnknownPolicyName, group.AttributeLocation, name));
                    }
                }
            }
        }
    }
}
