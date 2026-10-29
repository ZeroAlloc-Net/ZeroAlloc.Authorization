using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Discovery;

internal static class TypeAccess
{
    /// <summary>
    /// True when code in <paramref name="within"/> can name the type: the type, the types that
    /// contain it, and every type argument or element type in it are accessible, InternalsVisibleTo
    /// included.
    /// </summary>
    public static bool IsAccessible(ITypeSymbol type, IAssemblySymbol within, Compilation compilation)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return IsAccessible(array.ElementType, within, compilation);
            case IPointerTypeSymbol pointer:
                return IsAccessible(pointer.PointedAtType, within, compilation);
            case ITypeParameterSymbol:
                return true;
            case INamedTypeSymbol named:
                for (var current = named; current is not null; current = current.ContainingType)
                {
                    if (!compilation.IsSymbolAccessibleWithin(current.OriginalDefinition, within)) return false;
                    foreach (var arg in current.TypeArguments)
                    {
                        if (!IsAccessible(arg, within, compilation)) return false;
                    }
                }
                return true;
            default:
                return compilation.IsSymbolAccessibleWithin(type, within);
        }
    }
}
