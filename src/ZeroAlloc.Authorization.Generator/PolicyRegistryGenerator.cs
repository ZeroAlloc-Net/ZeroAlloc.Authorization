using System.Collections.Generic;
using System.Collections.Immutable;
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
        // Types in this compilation, found by their attributes. An edit reruns the transforms of
        // the attributed types only, and their value models compare equal unless the edit changed
        // them.
        var sourcePolicies = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                PolicySymbolWalker.PolicyAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, _) => PolicySymbolWalker.Discover(ctx.TargetSymbol, ctx.SemanticModel.Compilation))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!)
            .WithTrackingName(TrackingNames.SourcePolicies);

        var sourceRequirePolicies = FindRequires(context, RequireSymbolWalker.RequirePolicyAttributeFullName)
            .WithTrackingName(TrackingNames.SourceRequirePolicies);
        var sourceRequireAnyPolicies = FindRequires(context, RequireSymbolWalker.RequireAnyPolicyAttributeFullName)
            .WithTrackingName(TrackingNames.SourceRequireAnyPolicies);

        // Types in the referenced assemblies. Walking them visits every type they declare, so the
        // walk is keyed on the references alone and reruns only when they change.
        var references = context.MetadataReferencesProvider
            .Collect()
            .Select(static (refs, ct) => ReferenceScan.Run(refs, ct))
            .WithTrackingName(TrackingNames.ReferenceScan);

        var model = sourcePolicies.Collect()
            .Combine(sourceRequirePolicies.Collect())
            .Combine(sourceRequireAnyPolicies.Collect())
            .Combine(references)
            .Select(static (x, _) => AuthorizationModel.Build(x.Left.Left.Left, x.Left.Left.Right, x.Left.Right, x.Right));

        // The source is emitted from the models with their locations stripped, so an edit that
        // only moves a policy or a request reruns the diagnostics but leaves the source cached.
        var emitInputs = model
            .Select(static (m, _) => EmitInput.From(m))
            .WithTrackingName(TrackingNames.EmitInputs);
        context.RegisterSourceOutput(emitInputs, static (spc, input) => Emit(spc, input));

        var diagnosticInputs = model
            .Select(static (m, _) => Diagnose(m))
            .WithTrackingName(TrackingNames.DiagnosticInputs);
        context.RegisterSourceOutput(diagnosticInputs, static (spc, diagnostics) =>
        {
            foreach (var diagnostic in diagnostics)
            {
                spc.ReportDiagnostic(diagnostic.ToDiagnostic());
            }
        });
    }

    private static IncrementalValuesProvider<RequireDiscovery> FindRequires(
        IncrementalGeneratorInitializationContext context,
        string attributeFullName) =>
        context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, _) => RequireSymbolWalker.Discover(ctx.TargetSymbol, ctx.SemanticModel.Compilation))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!);

    private static void Emit(SourceProductionContext spc, EmitInput input)
    {
        if (input.Policies.Count == 0 && input.Requires.Count == 0) return;

        var authorizers = AuthorizerForEmitter.Emit(input.Requires, BuildPolicyByNameMap(input.Policies));
        var registration = DIRegistrationEmitter.Emit(input.Policies, input.Requires);

        spc.AddSource("ZeroAllocAuthorization.Generated.g.cs", SourceText.From(authorizers + registration, System.Text.Encoding.UTF8));
    }

    private static EquatableArray<DiagnosticInfo> Diagnose(AuthorizationModel model)
    {
        var diagnostics = new List<DiagnosticInfo>();
        var policies = new List<PolicyInfo>();
        foreach (var found in model.Policies)
        {
            foreach (var diagnostic in found.Diagnostics) diagnostics.Add(diagnostic);
            if (found.Policy is { } policy) policies.Add(policy);
        }

        var requires = new List<RequireInfo>();
        foreach (var found in model.Requires)
        {
            foreach (var diagnostic in found.Diagnostics) diagnostics.Add(diagnostic);
            if (found.Require is { } require) requires.Add(require);
        }

        if (policies.Count > 0 || requires.Count > 0)
        {
            ReportDuplicatePolicyNames(diagnostics, policies);
            var byName = BuildPolicyByNameMap(policies);
            ReportUnknownPolicyReferences(diagnostics, requires, byName);
            AuthorizerForEmitter.ReportArgShapeMismatches(requires, byName, diagnostics);
        }

        return new EquatableArray<DiagnosticInfo>(diagnostics.ToArray());
    }

    // ZAUTH002: detect duplicate [Policy] names before building byName. Reported once per name, at
    // the later [Policy] attribute in this compilation, with the others here as additional
    // locations. A clash between referenced assemblies only has no location in this compilation.
    private static void ReportDuplicatePolicyNames(List<DiagnosticInfo> diagnostics, IReadOnlyList<PolicyInfo> policies)
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

            var located = new List<LocationInfo>(same.Count);
            foreach (var policy in same)
            {
                if (policy.AttributeLocation is { } location) located.Add(location);
            }
            located.Sort(LocationInfo.Compare);

            LocationInfo? primary = null;
            if (located.Count > 0)
            {
                primary = located[located.Count - 1];
                located.RemoveAt(located.Count - 1);
            }
            diagnostics.Add(new DiagnosticInfo(
                Descriptors.DuplicatePolicyName,
                primary,
                new EquatableArray<LocationInfo>(located.ToArray()),
                new EquatableArray<string>(new[] { name })));
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
        List<DiagnosticInfo> diagnostics,
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
                        diagnostics.Add(DiagnosticInfo.Create(Descriptors.UnknownPolicyName, group.AttributeLocation, name));
                    }
                }
            }
        }
    }

    /// <summary>
    /// What the source is emitted from: the policies and requests without their locations.
    /// </summary>
    private sealed record EmitInput(EquatableArray<PolicyInfo> Policies, EquatableArray<RequireInfo> Requires)
    {
        public static EmitInput From(AuthorizationModel model)
        {
            var policies = new List<PolicyInfo>();
            foreach (var found in model.Policies)
            {
                if (found.Policy is { } policy) policies.Add(policy with { AttributeLocation = null });
            }

            var requires = new List<RequireInfo>();
            foreach (var found in model.Requires)
            {
                if (found.Require is not { } require) continue;
                var groups = new RequireGroup[require.Groups.Count];
                for (var i = 0; i < groups.Length; i++)
                {
                    groups[i] = require.Groups[i] with { AttributeLocation = null };
                }
                requires.Add(require with { Groups = new EquatableArray<RequireGroup>(groups) });
            }

            return new EmitInput(
                new EquatableArray<PolicyInfo>(policies.ToArray()),
                new EquatableArray<RequireInfo>(requires.ToArray()));
        }
    }
}
