using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Discovery;

internal static class RequireSymbolWalker
{
    private const string RequirePolicyAttributeFullName = "ZeroAlloc.Authorization.RequirePolicyAttribute";

    public static IReadOnlyList<RequireInfo> Find(Compilation compilation)
    {
        var requireAttr = compilation.GetTypeByMetadataName(RequirePolicyAttributeFullName);
        if (requireAttr is null) return System.Array.Empty<RequireInfo>();

        var results = new List<RequireInfo>();
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

                    List<string>? names = null;
                    foreach (var a in type.GetAttributes())
                    {
                        if (!SymbolEqualityComparer.Default.Equals(a.AttributeClass, requireAttr)) continue;
                        if (a.ConstructorArguments.Length == 0) continue;
                        if (a.ConstructorArguments[0].Value is not string name) continue;
                        if (string.IsNullOrEmpty(name)) continue;
                        names ??= new List<string>();
                        names.Add(name);
                    }
                    if (names is null || names.Count == 0) continue;

                    var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var unqualified = type.ToDisplayString(new SymbolDisplayFormat(
                        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));
                    var safe = SanitizeIdentifier(unqualified);
                    results.Add(new RequireInfo(fqn, safe, names));
                }
            }
        }
        return results;
    }

    private static string SanitizeIdentifier(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        }
        return sb.ToString();
    }
}
