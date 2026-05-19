using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization;

/// <summary>
/// Per-request authorization dispatcher. The source generator emits one
/// concrete subclass per <see cref="RequirePolicyAttribute"/>-decorated type.
/// Consumers resolve via <c>IServiceProvider.GetService&lt;AuthorizerFor&lt;TRequest&gt;&gt;()</c>.
/// </summary>
/// <typeparam name="TRequest">The request type whose policies this dispatcher evaluates.</typeparam>
public abstract class AuthorizerFor<TRequest>
{
    public abstract ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default);
}
