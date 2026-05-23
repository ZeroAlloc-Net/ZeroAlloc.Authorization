using System.Collections.Generic;

namespace ZeroAlloc.Authorization.Generator.Discovery;

internal sealed record RequireInfo(
    string FullyQualifiedTypeName,
    string SafeIdentifier,
    IReadOnlyList<RequireGroup> Groups);
