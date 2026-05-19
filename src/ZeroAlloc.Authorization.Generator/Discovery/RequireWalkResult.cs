using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Discovery;

internal sealed record RequireWalkResult(
    IReadOnlyList<RequireInfo> Requires,
    IReadOnlyList<Diagnostic> Diagnostics);
