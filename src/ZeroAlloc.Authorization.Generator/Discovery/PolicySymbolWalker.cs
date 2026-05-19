using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Discovery;

internal static class PolicySymbolWalker
{
    private const string PolicyAttributeFullName = "ZeroAlloc.Authorization.PolicyAttribute";
    private const string AuthorizationPolicyInterfaceFullName = "ZeroAlloc.Authorization.IAuthorizationPolicy";

    public static IReadOnlyList<PolicyInfo> Find(Compilation compilation)
    {
        var policyAttr = compilation.GetTypeByMetadataName(PolicyAttributeFullName);
        if (policyAttr is null) return System.Array.Empty<PolicyInfo>();

        var results = new List<PolicyInfo>();
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(compilation.SourceModule.GlobalNamespace);
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

                    AttributeData? policyAttribute = null;
                    foreach (var a in type.GetAttributes())
                    {
                        if (SymbolEqualityComparer.Default.Equals(a.AttributeClass, policyAttr))
                        {
                            policyAttribute = a;
                            break;
                        }
                    }
                    if (policyAttribute is null) continue;
                    if (policyAttribute.ConstructorArguments.Length == 0) continue;
                    var nameArg = policyAttribute.ConstructorArguments[0];
                    if (nameArg.Value is not string name) continue;
                    var instantiable = !type.IsAbstract && !type.IsStatic;
                    results.Add(new PolicyInfo(
                        name,
                        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        instantiable));
                }
            }
        }
        return results;
    }
}
