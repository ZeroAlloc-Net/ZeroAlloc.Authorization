namespace ZeroAlloc.Authorization;

/// <summary>Pluggable authorization rule. Implementations override <see cref="IsAuthorized"/>
/// for sync checks; override <see cref="IsAuthorizedAsync"/> for I/O-bound checks (e.g. tenant
/// lookup, claims validation against an external source). The default async implementation
/// delegates to the sync method.</summary>
public interface IAuthorizationPolicy
{
    /// <summary>Returns true if the caller is allowed. Hosts may pass a richer subinterface
    /// (e.g. <c>IToolCallSecurityContext</c>); downcast inside the policy body.</summary>
    bool IsAuthorized(ISecurityContext ctx);

    /// <summary>I/O-bound override point. Default delegates to <see cref="IsAuthorized"/>.
    /// Override to perform asynchronous lookups; honor <paramref name="ct"/> for cancellation.</summary>
    ValueTask<bool> IsAuthorizedAsync(ISecurityContext ctx, CancellationToken ct = default)
        => ValueTask.FromResult(IsAuthorized(ctx));
}
