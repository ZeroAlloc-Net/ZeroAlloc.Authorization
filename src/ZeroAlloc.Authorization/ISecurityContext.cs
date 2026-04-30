namespace ZeroAlloc.Authorization;

/// <summary>Caller identity for authorization decisions. Hosts downcast to richer
/// subinterfaces (e.g. tool-call context, request context) inside the policy body.</summary>
public interface ISecurityContext
{
    /// <summary>Stable caller identifier — user, agent, or service name. Non-null,
    /// non-empty, stable for the lifetime of this context object.</summary>
    string Id { get; }

    /// <summary>Role membership of the caller. Empty for anonymous callers.</summary>
    IReadOnlySet<string> Roles { get; }

    /// <summary>Optional claims (tenant, scope, sub, etc.). Empty by default.</summary>
    IReadOnlyDictionary<string, string> Claims { get; }
}
