using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Discovery;

/// <summary>
/// One [Policy("...")] class. v2.1 captures arity (0 = parameterless,
/// 1/2/3 = generic IAuthorizationPolicy{T..}) and the concrete type arguments
/// from the implemented interface. <see cref="IsInstantiable"/> is preserved
/// to keep ZAUTH004 abstract/static skip-emit behaviour byte-identical.
/// </summary>
internal sealed record PolicyInfo(
    string FullyQualifiedTypeName,
    string PolicyName,
    int Arity,
    IReadOnlyList<ITypeSymbol> TypeArgs,
    bool IsInstantiable);
