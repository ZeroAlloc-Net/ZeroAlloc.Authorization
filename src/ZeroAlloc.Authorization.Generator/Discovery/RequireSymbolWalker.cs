using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Authorization.Generator.Diagnostics;

namespace ZeroAlloc.Authorization.Generator.Discovery;

internal static class RequireSymbolWalker
{
    public const string RequirePolicyAttributeFullName    = "ZeroAlloc.Authorization.RequirePolicyAttribute";
    public const string RequireAnyPolicyAttributeFullName = "ZeroAlloc.Authorization.RequireAnyPolicyAttribute";

    /// <summary>
    /// The request on one type of this compilation, or null when it carries no [Require...].
    /// All of the type's [RequirePolicy] and [RequireAnyPolicy] attributes are read, in order,
    /// whichever of them found the type.
    /// </summary>
    public static RequireDiscovery? Discover(ISymbol symbol, Compilation compilation)
    {
        if (symbol is not INamedTypeSymbol type) return null;
        var context = WalkContext.Create(compilation);
        return context is null ? null : ProcessType(type, context);
    }

    /// <summary>
    /// The requests declared in the compilation's referenced assemblies, in walk order.
    /// </summary>
    public static EquatableArray<RequireDiscovery> FindInReferences(Compilation compilation, CancellationToken ct)
    {
        var context = WalkContext.Create(compilation);
        if (context is null) return EquatableArray<RequireDiscovery>.Empty;

        var results = new List<RequireDiscovery>();
        foreach (var refAsm in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            TypeWalker.Walk(refAsm.GlobalNamespace, type =>
            {
                if (ProcessType(type, context) is { } found) results.Add(found);
            }, ct);
        }
        return new EquatableArray<RequireDiscovery>(results.ToArray());
    }

    private sealed record WalkContext(
        Compilation Compilation,
        INamedTypeSymbol RequireAttr,
        INamedTypeSymbol? RequireAnyAttr)
    {
        public static WalkContext? Create(Compilation compilation)
        {
            var requireAttr = compilation.GetTypeByMetadataName(RequirePolicyAttributeFullName);
            if (requireAttr is null) return null;
            return new WalkContext(compilation, requireAttr, compilation.GetTypeByMetadataName(RequireAnyPolicyAttributeFullName));
        }
    }

    private static RequireDiscovery? ProcessType(INamedTypeSymbol type, WalkContext context)
    {
        List<RequireGroup>? groups = null;
        List<DiagnosticInfo>? diagnostics = null;

        foreach (var a in type.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(a.AttributeClass, context.RequireAttr))
            {
                TryAddRequireGroup(a, context.Compilation, ref groups);
            }
            else if (context.RequireAnyAttr is not null && SymbolEqualityComparer.Default.Equals(a.AttributeClass, context.RequireAnyAttr))
            {
                TryAddRequireAnyGroup(a, type, context.Compilation, ref groups, ref diagnostics);
            }
        }

        if (groups is null || groups.Count == 0)
        {
            return diagnostics is null ? null : Found(null);
        }

        // ZAUTH005 (defensive): the [RequirePolicy] AttributeTargets restriction already
        // blocks interface/enum/delegate targets at the compiler. This is belt-and-suspenders.
        // At the type identifier.
        if (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct)
        {
            (diagnostics ??= new List<DiagnosticInfo>()).Add(DiagnosticInfo.Create(
                Descriptors.RequirePolicyInvalidTarget,
                SourceLocations.Of(type, context.Compilation),
                Fqn(type),
                type.TypeKind.ToString().ToLowerInvariant()));
            return Found(null);
        }

        var unqualified = type.ToDisplayString(new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));
        return Found(new RequireInfo(Fqn(type), SanitizeIdentifier(unqualified), new EquatableArray<RequireGroup>(groups.ToArray())));

        RequireDiscovery Found(RequireInfo? require) => new(
            Fqn(type),
            SourceLocations.Of(type, context.Compilation),
            require,
            diagnostics is null ? EquatableArray<DiagnosticInfo>.Empty : new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
    }

    private static string Fqn(INamedTypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static void TryAddRequireGroup(AttributeData a, Compilation compilation, ref List<RequireGroup>? groups)
    {
        if (a.ConstructorArguments.Length == 0) return;
        if (a.ConstructorArguments[0].Value is not string name || string.IsNullOrEmpty(name)) return;

        EquatableArray<ConstantArg>? argList = null;
        // [RequirePolicy("name", null)] passes a null array: no arguments, as the attribute sees it.
        if (a.ConstructorArguments.Length >= 2
            && a.ConstructorArguments[1].Kind == TypedConstantKind.Array
            && !a.ConstructorArguments[1].IsNull)
        {
            var values = a.ConstructorArguments[1].Values;
            var args = new ConstantArg[values.Length];
            for (var i = 0; i < args.Length; i++)
            {
                args[i] = new ConstantArg(
                    ConstantLiterals.Format(values[i]),
                    values[i].Type is { } argType ? TypeRef.From(argType) : null);
            }
            argList = new EquatableArray<ConstantArg>(args);
        }

        groups ??= new List<RequireGroup>();
        groups.Add(new RequireGroup(
            RequireGroupKind.All,
            new EquatableArray<string>(new[] { name }),
            new EquatableArray<EquatableArray<ConstantArg>?>(new[] { argList }),
            SourceLocations.Of(a, compilation)));
    }

    private static void TryAddRequireAnyGroup(
        AttributeData a,
        INamedTypeSymbol type,
        Compilation compilation,
        ref List<RequireGroup>? groups,
        ref List<DiagnosticInfo>? diagnostics)
    {
        var attributeLocation = SourceLocations.Of(a, compilation);
        if (a.ConstructorArguments.Length == 0) return;
        if (a.ConstructorArguments[0].Kind != TypedConstantKind.Array || a.ConstructorArguments[0].IsNull) return;
        var nameValues = a.ConstructorArguments[0].Values;

        var names = new List<string>(nameValues.Length);
        foreach (var v in nameValues)
        {
            if (v.Value is string n && !string.IsNullOrEmpty(n)) names.Add(n);
        }
        if (names.Count == 0) return;

        if (names.Count == 1)
        {
            (diagnostics ??= new List<DiagnosticInfo>()).Add(DiagnosticInfo.Create(
                Descriptors.RequireAnyPolicySingleName,
                attributeLocation,
                names[0], Fqn(type)));
        }

        groups ??= new List<RequireGroup>();
        groups.Add(new RequireGroup(
            RequireGroupKind.Any,
            new EquatableArray<string>(names.ToArray()),
            new EquatableArray<EquatableArray<ConstantArg>?>(new EquatableArray<ConstantArg>?[names.Count]),
            attributeLocation));
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
