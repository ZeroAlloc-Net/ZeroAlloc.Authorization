using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Authorization.Generator.Diagnostics;

namespace ZeroAlloc.Authorization.Generator.Discovery;

internal static class RequireSymbolWalker
{
    private const string RequirePolicyAttributeFullName = "ZeroAlloc.Authorization.RequirePolicyAttribute";

    public static RequireWalkResult Find(Compilation compilation)
    {
        var requireAttr = compilation.GetTypeByMetadataName(RequirePolicyAttributeFullName);
        if (requireAttr is null) return new RequireWalkResult(System.Array.Empty<RequireInfo>(), System.Array.Empty<Diagnostic>());

        var results = new List<RequireInfo>();
        var diagnostics = new List<Diagnostic>();
        WalkNamespace(compilation.SourceModule.GlobalNamespace, requireAttr, results, diagnostics);
        foreach (var refAsm in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            WalkNamespace(refAsm.GlobalNamespace, requireAttr, results, diagnostics);
        }
        return new RequireWalkResult(results, diagnostics);
    }

    private static void WalkNamespace(
        INamespaceOrTypeSymbol root,
        INamedTypeSymbol requireAttr,
        List<RequireInfo> sink,
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
                    ProcessType(type, requireAttr, sink, diagnostics);
                }
            }
        }
    }

    private static void ProcessType(
        INamedTypeSymbol type,
        INamedTypeSymbol requireAttr,
        List<RequireInfo> sink,
        List<Diagnostic> diagnostics)
    {
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
        if (names is null || names.Count == 0) return;

        var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // ZAUTH005 (defensive): the [RequirePolicy] AttributeTargets restriction already
        // blocks interface/enum/delegate targets at the compiler. This is belt-and-suspenders.
        if (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct)
        {
            diagnostics.Add(Diagnostic.Create(
                Descriptors.RequirePolicyInvalidTarget,
                Location.None,
                fqn,
                type.TypeKind.ToString().ToLowerInvariant()));
            return;
        }

        var unqualified = type.ToDisplayString(new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));
        var safe = SanitizeIdentifier(unqualified);
        sink.Add(new RequireInfo(fqn, safe, names));
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
