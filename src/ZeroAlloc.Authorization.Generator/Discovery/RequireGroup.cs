using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Discovery;

/// <summary>
/// One [Require...] group attached to a request type. v2.1 captures group kind
/// (single-AND-element vs OR-group), names, and constant args for parameterized policies.
/// <see cref="AttributeLocation"/> is the attribute, where ZAUTH001 and ZAUTH007 report, or
/// <see cref="Location.None"/> for a type declared outside this compilation.
/// </summary>
internal sealed record RequireGroup(
    RequireGroupKind Kind,
    IReadOnlyList<string> PolicyNames,
    IReadOnlyList<IReadOnlyList<TypedConstant>?> Args,
    Location AttributeLocation);
