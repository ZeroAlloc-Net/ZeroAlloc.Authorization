using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization;

/// <summary>
/// Parameterized authorization policy with one compile-time-constant argument.
/// The argument value is supplied at the call site via
/// <see cref="RequirePolicyAttribute(string, object?[])"/>.
/// </summary>
public interface IAuthorizationPolicy<in T1>
{
    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, T1 arg1, CancellationToken ct = default);
}

/// <summary>
/// Parameterized authorization policy with two compile-time-constant arguments.
/// </summary>
public interface IAuthorizationPolicy<in T1, in T2>
{
    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, T1 arg1, T2 arg2, CancellationToken ct = default);
}

/// <summary>
/// Parameterized authorization policy with three compile-time-constant arguments.
/// Beyond three, encode args as a single string or surface them via
/// <see cref="ISecurityContext.Claims"/>.
/// </summary>
public interface IAuthorizationPolicy<in T1, in T2, in T3>
{
    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, T1 arg1, T2 arg2, T3 arg3, CancellationToken ct = default);
}
