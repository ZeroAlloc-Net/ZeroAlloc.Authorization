using Microsoft.CodeAnalysis;
using ZeroAlloc.Authorization.Generator.Discovery;

namespace ZeroAlloc.Authorization.Generator.Diagnostics;

/// <summary>
/// A diagnostic as a value the pipeline can cache. <see cref="Diagnostic"/> holds a
/// <see cref="Location"/>, which does not compare by value, so the pipeline carries this and builds
/// the diagnostic when it reports it.
/// </summary>
internal sealed record DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    LocationInfo? Location,
    EquatableArray<LocationInfo> AdditionalLocations,
    EquatableArray<string> MessageArgs)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, LocationInfo? location, params string[] messageArgs) =>
        new(descriptor, location, EquatableArray<LocationInfo>.Empty, new EquatableArray<string>(messageArgs));

    public Diagnostic ToDiagnostic()
    {
        var additional = new Location[AdditionalLocations.Count];
        for (var i = 0; i < additional.Length; i++)
        {
            additional[i] = LocationInfo.ToLocation(AdditionalLocations[i]);
        }

        var args = new object[MessageArgs.Count];
        for (var i = 0; i < args.Length; i++)
        {
            args[i] = MessageArgs[i];
        }

        return Diagnostic.Create(Descriptor, LocationInfo.ToLocation(Location), additional, args);
    }
}
