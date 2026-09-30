using ZeroAlloc.Authorization.Generator.Diagnostics;

namespace ZeroAlloc.Authorization.Generator.Discovery;

/// <summary>
/// One [Require...] group attached to a request type. v2.1 captures group kind
/// (single-AND-element vs OR-group), names, and constant args for parameterized policies.
/// <see cref="Args"/> holds one entry per name: null for a parameterless call, else the arguments.
/// <see cref="AttributeLocation"/> is the attribute, where ZAUTH001 and ZAUTH007 report, or null
/// for a type declared outside this compilation and in the models the source is emitted from.
/// </summary>
internal sealed record RequireGroup(
    RequireGroupKind Kind,
    EquatableArray<string> PolicyNames,
    EquatableArray<EquatableArray<ConstantArg>?> Args,
    LocationInfo? AttributeLocation);
