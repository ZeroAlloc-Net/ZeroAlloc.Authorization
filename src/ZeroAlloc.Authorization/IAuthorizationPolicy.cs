using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization;

/// <summary>
/// An authorization policy — given a security context, evaluates whether the
/// caller is authorized. Implementations are typically scoped DI services.
/// Sync-completing policies return <c>new ValueTask&lt;...&gt;(syncResult)</c>
/// — allocation-free on the stack.
/// </summary>
public interface IAuthorizationPolicy
{
    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default);
}
