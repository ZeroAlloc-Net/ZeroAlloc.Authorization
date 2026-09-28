using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Diagnostics;

/// <summary>
/// Where the ZAUTH diagnostics are reported.
/// </summary>
/// <remarks>
/// <para>
/// The generator runs on the whole compilation each time, and reads locations from that
/// compilation's symbols, so no location is ever served from a cache. A source location in one of
/// the compilation's trees is what the IDE navigates to and what <c>#pragma warning disable</c>
/// applies to.
/// </para>
/// <para>
/// The walkers also visit referenced assemblies. In the IDE a project reference is a compilation
/// reference, whose symbols have source locations in the other project's trees. A generator may
/// only report locations in the compilation it runs on, and reporting another one fails the
/// generator, so code outside this compilation is reported with <see cref="Location.None"/>.
/// </para>
/// </remarks>
internal static class SourceLocations
{
    /// <summary>
    /// The symbol's first location in this compilation, or <see cref="Location.None"/>.
    /// </summary>
    public static Location Of(ISymbol symbol, Compilation compilation)
    {
        foreach (var location in symbol.Locations)
        {
            if (location.SourceTree is { } tree && compilation.ContainsSyntaxTree(tree)) return location;
        }

        return Location.None;
    }

    /// <summary>
    /// The attribute's syntax in this compilation, or <see cref="Location.None"/>.
    /// </summary>
    public static Location Of(AttributeData attribute, Compilation compilation) =>
        attribute.ApplicationSyntaxReference is { } reference && compilation.ContainsSyntaxTree(reference.SyntaxTree)
            ? Location.Create(reference.SyntaxTree, reference.Span)
            : Location.None;

    /// <summary>
    /// Orders locations by file path, then position, so "the later declaration" is the same on
    /// every run.
    /// </summary>
    public static int Compare(Location x, Location y)
    {
        var byPath = string.CompareOrdinal(x.SourceTree?.FilePath, y.SourceTree?.FilePath);
        return byPath != 0 ? byPath : x.SourceSpan.Start.CompareTo(y.SourceSpan.Start);
    }
}
