using ZeroAlloc.Authorization.Generator.Diagnostics;

namespace ZeroAlloc.Authorization.Generator.Discovery;

/// <summary>
/// One [Policy("...")] class. v2.1 captures arity (0 = parameterless,
/// 1/2/3 = generic IAuthorizationPolicy{T..}) and the concrete type arguments
/// from the implemented interface. <see cref="IsInstantiable"/> is preserved
/// to keep ZAUTH004 abstract/static skip-emit behaviour byte-identical.
/// <see cref="AttributeLocation"/> is the [Policy] attribute, where ZAUTH002 reports a clash, or
/// null for a policy declared outside this compilation and in the models the source is emitted
/// from.
/// </summary>
internal sealed record PolicyInfo(
    string FullyQualifiedTypeName,
    string PolicyName,
    int Arity,
    EquatableArray<TypeRef> TypeArgs,
    bool IsInstantiable,
    LocationInfo? AttributeLocation);
