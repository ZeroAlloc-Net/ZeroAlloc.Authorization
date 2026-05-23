using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Authorization.Generator.Diagnostics;

namespace ZeroAlloc.Authorization.Generator.Discovery;

internal static class PolicySymbolWalker
{
    private const string PolicyAttributeFullName       = "ZeroAlloc.Authorization.PolicyAttribute";
    private const string IAuthorizationPolicyFullName  = "ZeroAlloc.Authorization.IAuthorizationPolicy";
    private const string IAuthorizationPolicy1FullName = "ZeroAlloc.Authorization.IAuthorizationPolicy`1";
    private const string IAuthorizationPolicy2FullName = "ZeroAlloc.Authorization.IAuthorizationPolicy`2";
    private const string IAuthorizationPolicy3FullName = "ZeroAlloc.Authorization.IAuthorizationPolicy`3";

    public static PolicyWalkResult Find(Compilation compilation)
    {
        var policyAttr = compilation.GetTypeByMetadataName(PolicyAttributeFullName);
        if (policyAttr is null) return new PolicyWalkResult(System.Array.Empty<PolicyInfo>(), System.Array.Empty<Diagnostic>());

        var policyInterfaces = new INamedTypeSymbol?[4];
        policyInterfaces[0] = compilation.GetTypeByMetadataName(IAuthorizationPolicyFullName);
        policyInterfaces[1] = compilation.GetTypeByMetadataName(IAuthorizationPolicy1FullName);
        policyInterfaces[2] = compilation.GetTypeByMetadataName(IAuthorizationPolicy2FullName);
        policyInterfaces[3] = compilation.GetTypeByMetadataName(IAuthorizationPolicy3FullName);

        var results = new List<PolicyInfo>();
        var diagnostics = new List<Diagnostic>();
        WalkNamespace(compilation.SourceModule.GlobalNamespace, policyAttr, policyInterfaces, results, diagnostics);
        foreach (var refAsm in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            WalkNamespace(refAsm.GlobalNamespace, policyAttr, policyInterfaces, results, diagnostics);
        }
        return new PolicyWalkResult(results, diagnostics);
    }

    private static void WalkNamespace(
        INamespaceOrTypeSymbol root,
        INamedTypeSymbol policyAttr,
        INamedTypeSymbol?[] policyInterfaces,
        List<PolicyInfo> sink,
        List<Diagnostic> diagnostics)
    {
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();

            // Nested types pushed via type.GetTypeMembers() are popped here as INamedTypeSymbol.
            // The original walker only iterated their members; ProcessType was never called on the
            // nested type itself. Without this, [Policy] on a nested class is silently ignored.
            if (current is INamedTypeSymbol currentType)
            {
                ProcessType(currentType, policyAttr, policyInterfaces, sink, diagnostics);
            }

            foreach (var member in current.GetMembers())
            {
                if (member is INamespaceSymbol ns)
                {
                    stack.Push(ns);
                }
                else if (member is INamedTypeSymbol type)
                {
                    foreach (var nested in type.GetTypeMembers()) stack.Push(nested);
                    ProcessType(type, policyAttr, policyInterfaces, sink, diagnostics);
                }
            }
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

    private static void ProcessType(
        INamedTypeSymbol type,
        INamedTypeSymbol policyAttr,
        INamedTypeSymbol?[] policyInterfaces,
        List<PolicyInfo> sink,
        List<Diagnostic> diagnostics)
    {
        AttributeData? policyAttribute = null;
        foreach (var a in type.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(a.AttributeClass, policyAttr))
            {
                policyAttribute = a;
                break;
            }
        }
        if (policyAttribute is null) return;
        if (policyAttribute.ConstructorArguments.Length == 0) return;
        var nameArg = policyAttribute.ConstructorArguments[0];
        if (nameArg.Value is not string policyName) return;

        var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var match = FindPolicyInterfaces(type, policyInterfaces);

        // ZAUTH003: [Policy] class must implement IAuthorizationPolicy (any variant).
        if (match.VariantsCount == 0)
        {
            diagnostics.Add(Diagnostic.Create(
                Descriptors.PolicyDoesNotImplementInterface,
                Location.None,
                fqn));
            return;
        }

        // ZAUTH008: implementing more than one IAuthorizationPolicy variant is ambiguous.
        if (match.VariantsCount > 1)
        {
            diagnostics.Add(Diagnostic.Create(
                Descriptors.PolicyImplementsMultipleVariants,
                type.Locations.Length > 0 ? type.Locations[0] : Location.None,
                policyName,
                fqn,
                match.VariantsLabel));
            return;
        }

        var instantiable = !type.IsAbstract && !type.IsStatic;
        if (!instantiable)
        {
            // ZAUTH004: [Policy] class is abstract/static — DI cannot construct it.
            diagnostics.Add(Diagnostic.Create(
                Descriptors.PolicyNotInstantiable,
                Location.None,
                fqn));
        }

        var resolved = match.Parameterless ?? match.Generic!;
        var typeArgs = resolved.TypeArguments;
        var arity = match.Parameterless is not null ? 0 : match.GenericArity;

        sink.Add(new PolicyInfo(
            fqn,
            policyName,
            arity,
            typeArgs.ToArray(),
            instantiable));
    }
}
