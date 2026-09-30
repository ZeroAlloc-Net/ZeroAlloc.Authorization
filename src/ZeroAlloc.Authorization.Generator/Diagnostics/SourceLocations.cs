using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Diagnostics;

/// <summary>
/// Where the ZAUTH diagnostics are reported.
/// </summary>
/// <remarks>
/// <para>
/// A source location in one of the compilation's trees is what the IDE navigates to and what
/// <c>#pragma warning disable</c> applies to. The pipeline carries it as a
/// <see cref="LocationInfo"/>, which is part of the model's equality, so a model is only served
/// from the cache while its tree is unchanged.
/// </para>
/// <para>
/// Types in referenced assemblies are reported without a location. In the IDE a project reference
/// is a compilation reference, whose symbols have source locations in the other project's trees.
/// A generator may only report locations in the compilation it runs on, and reporting another one
/// fails the generator, so code outside this compilation is reported with
/// <see cref="Location.None"/>.
/// </para>
/// </remarks>
internal static class SourceLocations
{
    /// <summary>
    /// The symbol's first location in this compilation, or null.
    /// </summary>
    public static LocationInfo? Of(ISymbol symbol, Compilation compilation)
    {
        foreach (var location in symbol.Locations)
        {
            if (location.SourceTree is { } tree && compilation.ContainsSyntaxTree(tree))
            {
                return new LocationInfo(tree, location.SourceSpan);
            }
        }

        return null;
    }

    /// <summary>
    /// The attribute's syntax in this compilation, or null.
    /// </summary>
    public static LocationInfo? Of(AttributeData attribute, Compilation compilation) =>
        attribute.ApplicationSyntaxReference is { } reference && compilation.ContainsSyntaxTree(reference.SyntaxTree)
            ? new LocationInfo(reference.SyntaxTree, reference.Span)
            : null;
}
