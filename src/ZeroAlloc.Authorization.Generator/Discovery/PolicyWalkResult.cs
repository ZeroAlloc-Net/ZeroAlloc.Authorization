using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Discovery;

internal sealed record PolicyWalkResult(
    IReadOnlyList<PolicyInfo> Policies,
    IReadOnlyList<Diagnostic> Diagnostics);
