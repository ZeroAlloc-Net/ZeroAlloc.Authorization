namespace ZeroAlloc.Authorization;

/// <summary>Binds a method (e.g. an AIFunction-bound method or a Mediator request handler)
/// to a named <see cref="IAuthorizationPolicy"/>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AuthorizeAttribute(string policyName) : Attribute
{
    /// <summary>Name of the policy this method requires (matches <see cref="AuthorizationPolicyAttribute.Name"/>).</summary>
    public string PolicyName { get; } = policyName;
}
