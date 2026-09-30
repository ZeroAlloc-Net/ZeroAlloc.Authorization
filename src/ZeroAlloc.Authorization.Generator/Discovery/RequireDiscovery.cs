using ZeroAlloc.Authorization.Generator.Diagnostics;

namespace ZeroAlloc.Authorization.Generator.Discovery;

/// <summary>
/// What the generator found on one type with [RequirePolicy] or [RequireAnyPolicy]: the request,
/// or null when it cannot have an authorizer, and the diagnostics about it.
/// <see cref="TypeLocation"/> orders the requests declared in this compilation.
/// </summary>
internal sealed record RequireDiscovery(
    string FullyQualifiedTypeName,
    LocationInfo? TypeLocation,
    RequireInfo? Require,
    EquatableArray<DiagnosticInfo> Diagnostics);
