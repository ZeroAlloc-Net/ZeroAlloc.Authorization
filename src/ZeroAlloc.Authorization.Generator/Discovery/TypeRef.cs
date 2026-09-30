using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Discovery;

/// <summary>
/// A type as the pipeline caches it: the names the emitter and the diagnostics print, and an
/// identity that stands in for symbol equality.
/// </summary>
/// <param name="FullyQualifiedName">The name with <c>global::</c>, for emitted code.</param>
/// <param name="DisplayName">The name as the diagnostics show it.</param>
/// <param name="Identity">
/// The fully qualified name and the name of the assembly that declares the type, so two types of
/// the same name from different assemblies do not compare equal.
/// </param>
internal sealed record TypeRef(string FullyQualifiedName, string DisplayName, string Identity)
{
    public static TypeRef From(ITypeSymbol type)
    {
        var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new TypeRef(fqn, type.ToDisplayString(), fqn + ", " + DeclaringAssembly(type));
    }

    private static string DeclaringAssembly(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => DeclaringAssembly(array.ElementType),
        IPointerTypeSymbol pointer => DeclaringAssembly(pointer.PointedAtType),
        _ => type.ContainingAssembly?.Identity.Name ?? string.Empty,
    };
}
