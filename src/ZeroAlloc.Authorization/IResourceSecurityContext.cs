namespace ZeroAlloc.Authorization;

/// <summary>
/// A security context that ALSO carries a typed resource — the thing being acted upon.
/// Hosts may implement this on top of <see cref="ISecurityContext"/> to expose the
/// dispatched request (or any other resource shape) to authorization policies that
/// need resource-typed checks.
/// </summary>
/// <typeparam name="TResource">
/// The resource type the host populates. Policies type-check via
/// <c>ctx is IResourceSecurityContext&lt;TPost&gt; rc</c> to access it.
/// </typeparam>
/// <remarks>
/// <para>v2.1 ships this contract; host packages (<c>ZeroAlloc.Mediator.Authorization</c>,
/// <c>AI.Sentinel</c>) adopt by populating the typed-resource context in their dispatch
/// behaviour as a follow-up. Until then, <c>ctx is IResourceSecurityContext&lt;T&gt;</c>
/// falls through to <c>false</c>.</para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class OwnerOnlyPolicy : IAuthorizationPolicy
/// {
///     public ValueTask&lt;UnitResult&lt;AuthorizationFailure&gt;&gt; EvaluateAsync(
///         ISecurityContext ctx, CancellationToken ct = default)
///         => new(ctx is IResourceSecurityContext&lt;Post&gt; rc &amp;&amp; rc.Resource.OwnerId == ctx.Id
///             ? UnitResult&lt;AuthorizationFailure&gt;.Success()
///             : new AuthorizationFailure("resource.not_owner"));
/// }
/// </code>
/// </example>
public interface IResourceSecurityContext<out TResource> : ISecurityContext
{
    /// <summary>The resource the request is acting upon.</summary>
    TResource Resource { get; }
}
