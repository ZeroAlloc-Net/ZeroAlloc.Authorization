namespace ZeroAlloc.Authorization;

/// <summary>Names a policy class so it can be referenced from <see cref="AuthorizeAttribute"/>
/// and host-specific binding APIs (e.g. AI.Sentinel's <c>RequireToolPolicy</c>).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AuthorizationPolicyAttribute(string name) : Attribute
{
    /// <summary>The name used to reference this policy.</summary>
    public string Name { get; } = name;
}
