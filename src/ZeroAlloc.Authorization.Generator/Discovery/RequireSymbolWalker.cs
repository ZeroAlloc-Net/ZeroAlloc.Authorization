using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Authorization.Generator.Diagnostics;

namespace ZeroAlloc.Authorization.Generator.Discovery;

internal static class RequireSymbolWalker
{
    private const string RequirePolicyAttributeFullName    = "ZeroAlloc.Authorization.RequirePolicyAttribute";
    private const string RequireAnyPolicyAttributeFullName = "ZeroAlloc.Authorization.RequireAnyPolicyAttribute";

    public static RequireWalkResult Find(Compilation compilation)
    {
        var requireAttr    = compilation.GetTypeByMetadataName(RequirePolicyAttributeFullName);
        var requireAnyAttr = compilation.GetTypeByMetadataName(RequireAnyPolicyAttributeFullName);
        if (requireAttr is null) return new RequireWalkResult(System.Array.Empty<RequireInfo>(), System.Array.Empty<Diagnostic>());

        var results = new List<RequireInfo>();
        var diagnostics = new List<Diagnostic>();
        WalkNamespace(compilation.SourceModule.GlobalNamespace, requireAttr, requireAnyAttr, results, diagnostics);
        foreach (var refAsm in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            WalkNamespace(refAsm.GlobalNamespace, requireAttr, requireAnyAttr, results, diagnostics);
        }
        return new RequireWalkResult(results, diagnostics);
    }

    private static void WalkNamespace(
        INamespaceOrTypeSymbol root,
        INamedTypeSymbol requireAttr,
        INamedTypeSymbol? requireAnyAttr,
        List<RequireInfo> sink,
        List<Diagnostic> diagnostics)
    {
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();

            // Nested types pushed via type.GetTypeMembers() are popped here as INamedTypeSymbol.
            // The original walker only iterated their members; ProcessType was never called on the
            // nested type itself. Without this, [RequirePolicy] on a nested class is silently ignored.
            if (current is INamedTypeSymbol currentType)
            {
                ProcessType(currentType, requireAttr, requireAnyAttr, sink, diagnostics);
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
                    ProcessType(type, requireAttr, requireAnyAttr, sink, diagnostics);
                }
            }
        }
    }

    private static void ProcessType(
        INamedTypeSymbol type,
        INamedTypeSymbol requireAttr,
        INamedTypeSymbol? requireAnyAttr,
        List<RequireInfo> sink,
        List<Diagnostic> diagnostics)
    {
        List<RequireGroup>? groups = null;

        foreach (var a in type.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(a.AttributeClass, requireAttr))
            {
                TryAddRequireGroup(a, ref groups);
            }
            else if (requireAnyAttr is not null && SymbolEqualityComparer.Default.Equals(a.AttributeClass, requireAnyAttr))
            {
                TryAddRequireAnyGroup(a, type, ref groups, diagnostics);
            }
        }

        if (groups is null || groups.Count == 0) return;

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
        sink.Add(new RequireInfo(fqn, safe, groups));
    }

    private static void TryAddRequireGroup(AttributeData a, ref List<RequireGroup>? groups)
    {
        if (a.ConstructorArguments.Length == 0) return;
        if (a.ConstructorArguments[0].Value is not string name || string.IsNullOrEmpty(name)) return;

        IReadOnlyList<TypedConstant>? argList = null;
        if (a.ConstructorArguments.Length >= 2 && a.ConstructorArguments[1].Kind == TypedConstantKind.Array)
        {
            argList = a.ConstructorArguments[1].Values;
        }

        groups ??= new List<RequireGroup>();
        groups.Add(new RequireGroup(
            RequireGroupKind.All,
            new[] { name },
            new IReadOnlyList<TypedConstant>?[] { argList },
            a.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None));
    }

    private static void TryAddRequireAnyGroup(
        AttributeData a,
        INamedTypeSymbol type,
        ref List<RequireGroup>? groups,
        List<Diagnostic> diagnostics)
    {
        if (a.ConstructorArguments.Length == 0) return;
        if (a.ConstructorArguments[0].Kind != TypedConstantKind.Array) return;
        var nameValues = a.ConstructorArguments[0].Values;

        var names = new List<string>(nameValues.Length);
        foreach (var v in nameValues)
        {
            if (v.Value is string n && !string.IsNullOrEmpty(n)) names.Add(n);
        }
        if (names.Count == 0) return;

        if (names.Count == 1)
        {
            diagnostics.Add(Diagnostic.Create(
                Descriptors.RequireAnyPolicySingleName,
                a.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None,
                names[0], type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }

        var argsPerName = new IReadOnlyList<TypedConstant>?[names.Count];
        groups ??= new List<RequireGroup>();
        groups.Add(new RequireGroup(
            RequireGroupKind.Any,
            names,
            argsPerName,
            a.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None));
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
