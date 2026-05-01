namespace ZeroAlloc.Authorization;

/// <summary>Structured deny information returned from <see cref="IAuthorizationPolicy.Evaluate"/>.
/// Hosts surface <see cref="Code"/> for machine-readable matching and <see cref="Reason"/> for
/// optional human-readable text.</summary>
public readonly struct AuthorizationFailure
{
    /// <summary>Default deny code emitted when an <c>IsAuthorized=false</c> result is wrapped
    /// without a more specific code. Hosts can match on this to detect "policy denied without
    /// explanation" vs an explicitly-coded deny.</summary>
    public const string DefaultDenyCode = "policy.deny";

    private readonly string? _code;

    /// <summary>Machine-readable code, e.g. "policy.deny.role" or "tenant.inactive".
    /// Non-null, non-empty.</summary>
    public string Code => _code ?? DefaultDenyCode;

    /// <summary>Optional human-readable reason. Hosts may surface this in API responses
    /// or logs; treat as untrusted-for-display unless the policy author guarantees it.</summary>
    public string? Reason { get; }

    public AuthorizationFailure(string code, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        _code = code;
        Reason = reason;
    }
}
