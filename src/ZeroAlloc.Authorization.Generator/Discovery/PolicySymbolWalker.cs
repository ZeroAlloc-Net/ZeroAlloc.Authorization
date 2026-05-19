using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Authorization.Generator.Diagnostics;

namespace ZeroAlloc.Authorization.Generator.Discovery;

internal static class PolicySymbolWalker
{
    private const string PolicyAttributeFullName = "ZeroAlloc.Authorization.PolicyAttribute";
    private const string AuthorizationPolicyInterfaceFullName = "ZeroAlloc.Authorization.IAuthorizationPolicy";

    public static PolicyWalkResult Find(Compilation compilation)
    {
        var policyAttr = compilation.GetTypeByMetadataName(PolicyAttributeFullName);
        if (policyAttr is null) return new PolicyWalkResult(System.Array.Empty<PolicyInfo>(), System.Array.Empty<Diagnostic>());

        var policyIface = compilation.GetTypeByMetadataName(AuthorizationPolicyInterfaceFullName);

        var results = new List<PolicyInfo>();
        var diagnostics = new List<Diagnostic>();
        WalkNamespace(compilation.SourceModule.GlobalNamespace, policyAttr, policyIface, results, diagnostics);
        foreach (var refAsm in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            WalkNamespace(refAsm.GlobalNamespace, policyAttr, policyIface, results, diagnostics);
        }
        return new PolicyWalkResult(results, diagnostics);
    }

    private static void WalkNamespace(
        INamespaceOrTypeSymbol root,
        INamedTypeSymbol policyAttr,
        INamedTypeSymbol? policyIface,
        List<PolicyInfo> sink,
        List<Diagnostic> diagnostics)
    {
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var member in current.GetMembers())
            {
                if (member is INamespaceSymbol ns)
                {
                    stack.Push(ns);
                }
                else if (member is INamedTypeSymbol type)
                {
                    foreach (var nested in type.GetTypeMembers()) stack.Push(nested);
                    ProcessType(type, policyAttr, policyIface, sink, diagnostics);
                }
            }
        }
    }

    private static void ProcessType(
        INamedTypeSymbol type,
        INamedTypeSymbol policyAttr,
        INamedTypeSymbol? policyIface,
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
        if (nameArg.Value is not string name) return;

        // ZAUTH003: [Policy] class must implement IAuthorizationPolicy.
        if (policyIface is not null && !ImplementsInterface(type, policyIface))
        {
            diagnostics.Add(Diagnostic.Create(
                Descriptors.PolicyDoesNotImplementInterface,
                Location.None,
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            return;
        }

        var instantiable = !type.IsAbstract && !type.IsStatic;
        var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!instantiable)
        {
            // ZAUTH004: [Policy] class is abstract/static — DI cannot construct it.
            diagnostics.Add(Diagnostic.Create(
                Descriptors.PolicyNotInstantiable,
                Location.None,
                fqn));
        }
        sink.Add(new PolicyInfo(name, fqn, instantiable));
    }

    private static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol iface)
    {
        foreach (var i in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(i, iface)) return true;
        }
        return false;
    }
}

