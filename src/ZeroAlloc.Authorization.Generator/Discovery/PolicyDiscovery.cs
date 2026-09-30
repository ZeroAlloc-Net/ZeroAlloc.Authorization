using ZeroAlloc.Authorization.Generator.Diagnostics;

namespace ZeroAlloc.Authorization.Generator.Discovery;

/// <summary>
/// What the generator found on one [Policy] type: the policy, or null when the type cannot be one,
/// and the diagnostics about it.
/// </summary>
internal sealed record PolicyDiscovery(
    string FullyQualifiedTypeName,
    PolicyInfo? Policy,
    EquatableArray<DiagnosticInfo> Diagnostics);
