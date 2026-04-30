namespace ZeroAlloc.Authorization;

/// <summary>Names a policy class so it can be referenced from <see cref="AuthorizeAttribute"/>
/// and host-specific binding APIs (e.g. AI.Sentinel's <c>RequireToolPolicy</c>).</summary>
[AttributeUsage(
    AttributeTargets.Class,
    AllowMultiple = false,
    Inherited = false)]
public sealed class AuthorizationPolicyAttribute : Attribute
{
    /// <summary>Creates an <see cref="AuthorizationPolicyAttribute"/> with the given name.</summary>
    /// <param name="name">Non-null, non-empty policy name.</param>
    public AuthorizationPolicyAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>The name used to reference this policy.</summary>
    public string Name { get; }
}
