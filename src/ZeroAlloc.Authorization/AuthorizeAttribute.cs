namespace ZeroAlloc.Authorization;

/// <summary>Binds a method or class (e.g. an AIFunction-bound method or a Mediator request handler)
/// to a named <see cref="IAuthorizationPolicy"/>.</summary>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Class,
    AllowMultiple = true,
    Inherited = true)]
public sealed class AuthorizeAttribute : Attribute
{
    /// <summary>Creates an <see cref="AuthorizeAttribute"/> bound to the named policy.</summary>
    /// <param name="policyName">Non-null, non-empty policy name. Matches
    /// <see cref="AuthorizationPolicyAttribute.Name"/>.</param>
    public AuthorizeAttribute(string policyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(policyName);
        PolicyName = policyName;
    }

    /// <summary>Name of the policy this method requires (matches <see cref="AuthorizationPolicyAttribute.Name"/>).</summary>
    public string PolicyName { get; }
}
