namespace ZeroAlloc.Authorization.Generator.Discovery;

internal sealed record RequireInfo(
    string FullyQualifiedTypeName,
    string SafeIdentifier,
    EquatableArray<RequireGroup> Groups);
