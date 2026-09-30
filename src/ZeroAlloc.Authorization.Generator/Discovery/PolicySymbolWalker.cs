using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Authorization.Generator.Diagnostics;

namespace ZeroAlloc.Authorization.Generator.Discovery;

internal static class PolicySymbolWalker
{
    public const string PolicyAttributeFullName        = "ZeroAlloc.Authorization.PolicyAttribute";
    private const string IAuthorizationPolicyFullName  = "ZeroAlloc.Authorization.IAuthorizationPolicy";
    private const string IAuthorizationPolicy1FullName = "ZeroAlloc.Authorization.IAuthorizationPolicy`1";
    private const string IAuthorizationPolicy2FullName = "ZeroAlloc.Authorization.IAuthorizationPolicy`2";
    private const string IAuthorizationPolicy3FullName = "ZeroAlloc.Authorization.IAuthorizationPolicy`3";

    /// <summary>
    /// The policy on one type of this compilation, or null when the type carries no [Policy].
    /// </summary>
    public static PolicyDiscovery? Discover(ISymbol symbol, Compilation compilation)
    {
        if (symbol is not INamedTypeSymbol type) return null;
        var context = WalkContext.Create(compilation);
        return context is null ? null : ProcessType(type, context);
    }

    /// <summary>
    /// The policies declared in the compilation's referenced assemblies, in walk order.
    /// </summary>
    public static EquatableArray<PolicyDiscovery> FindInReferences(Compilation compilation, CancellationToken ct)
    {
        var context = WalkContext.Create(compilation);
        if (context is null) return EquatableArray<PolicyDiscovery>.Empty;

        var results = new List<PolicyDiscovery>();
        foreach (var refAsm in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            TypeWalker.Walk(refAsm.GlobalNamespace, type =>
            {
                if (ProcessType(type, context) is { } found) results.Add(found);
            }, ct);
        }
        return new EquatableArray<PolicyDiscovery>(results.ToArray());
    }

    private sealed record WalkContext(
        Compilation Compilation,
        INamedTypeSymbol PolicyAttr,
        INamedTypeSymbol?[] PolicyInterfaces)
    {
        public static WalkContext? Create(Compilation compilation)
        {
            var policyAttr = compilation.GetTypeByMetadataName(PolicyAttributeFullName);
            if (policyAttr is null) return null;

            var policyInterfaces = new INamedTypeSymbol?[4];
            policyInterfaces[0] = compilation.GetTypeByMetadataName(IAuthorizationPolicyFullName);
            policyInterfaces[1] = compilation.GetTypeByMetadataName(IAuthorizationPolicy1FullName);
            policyInterfaces[2] = compilation.GetTypeByMetadataName(IAuthorizationPolicy2FullName);
            policyInterfaces[3] = compilation.GetTypeByMetadataName(IAuthorizationPolicy3FullName);
            return new WalkContext(compilation, policyAttr, policyInterfaces);
        }
    }

    private readonly record struct InterfaceMatch(
        INamedTypeSymbol? Parameterless,
        INamedTypeSymbol? Generic,
        int GenericArity,
        int VariantsCount,
        string VariantsLabel);

    private static InterfaceMatch FindPolicyInterfaces(INamedTypeSymbol type, INamedTypeSymbol?[] policyInterfaces)
    {
        INamedTypeSymbol? implParameterless = null;
        INamedTypeSymbol? implGeneric = null;
        int implGenericArity = 0;
        var implVariantsCount = 0;
        var implVariantsLabel = new System.Text.StringBuilder();

        foreach (var i in type.AllInterfaces)
        {
            var iOrig = i.OriginalDefinition;
            for (int a = 0; a <= 3; a++)
            {
                if (policyInterfaces[a] is null) continue;
                if (SymbolEqualityComparer.Default.Equals(iOrig, policyInterfaces[a]))
                {
                    if (a == 0) implParameterless = i;
                    else { implGeneric = i; implGenericArity = a; }
                    implVariantsCount++;
                    if (implVariantsLabel.Length > 0) implVariantsLabel.Append(", ");
                    implVariantsLabel.Append(a == 0 ? "IAuthorizationPolicy" : $"IAuthorizationPolicy`{a}");
                    break;
                }
            }
        }

        return new InterfaceMatch(implParameterless, implGeneric, implGenericArity, implVariantsCount, implVariantsLabel.ToString());
    }

    private static AttributeData? FindPolicyAttribute(INamedTypeSymbol type, INamedTypeSymbol policyAttr)
    {
        foreach (var a in type.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(a.AttributeClass, policyAttr)) return a;
        }
        return null;
    }

    // ZAUTH003, ZAUTH004 and ZAUTH008 are about the class, and are reported at its identifier.
    private static PolicyDiscovery? ProcessType(INamedTypeSymbol type, WalkContext context)
    {
        var policyAttribute = FindPolicyAttribute(type, context.PolicyAttr);
        if (policyAttribute is null) return null;
        if (policyAttribute.ConstructorArguments.Length == 0) return null;
        var nameArg = policyAttribute.ConstructorArguments[0];
        if (nameArg.Value is not string policyName) return null;

        var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var match = FindPolicyInterfaces(type, context.PolicyInterfaces);
        var typeLocation = SourceLocations.Of(type, context.Compilation);

        // ZAUTH003: [Policy] class must implement IAuthorizationPolicy (any variant).
        if (match.VariantsCount == 0)
        {
            return Rejected(fqn, DiagnosticInfo.Create(
                Descriptors.PolicyDoesNotImplementInterface,
                typeLocation,
                fqn));
        }

        // ZAUTH008: implementing more than one IAuthorizationPolicy variant is ambiguous.
        if (match.VariantsCount > 1)
        {
            return Rejected(fqn, DiagnosticInfo.Create(
                Descriptors.PolicyImplementsMultipleVariants,
                typeLocation,
                policyName,
                fqn,
                match.VariantsLabel));
        }

        var diagnostics = EquatableArray<DiagnosticInfo>.Empty;
        var instantiable = !type.IsAbstract && !type.IsStatic;
        if (!instantiable)
        {
            // ZAUTH004: [Policy] class is abstract/static — DI cannot construct it.
            diagnostics = new EquatableArray<DiagnosticInfo>(new[]
            {
                DiagnosticInfo.Create(Descriptors.PolicyNotInstantiable, typeLocation, fqn),
            });
        }

        var resolved = match.Parameterless ?? match.Generic!;
        var typeArgs = new TypeRef[resolved.TypeArguments.Length];
        for (var i = 0; i < typeArgs.Length; i++)
        {
            typeArgs[i] = TypeRef.From(resolved.TypeArguments[i]);
        }
        var arity = match.Parameterless is not null ? 0 : match.GenericArity;

        var policy = new PolicyInfo(
            fqn,
            policyName,
            arity,
            new EquatableArray<TypeRef>(typeArgs),
            instantiable,
            SourceLocations.Of(policyAttribute, context.Compilation));
        return new PolicyDiscovery(fqn, policy, diagnostics);
    }

    private static PolicyDiscovery Rejected(string fqn, DiagnosticInfo diagnostic) =>
        new(fqn, null, new EquatableArray<DiagnosticInfo>(new[] { diagnostic }));
}
