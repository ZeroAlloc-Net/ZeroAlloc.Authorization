using System;

namespace ZeroAlloc.Authorization;

/// <summary>
/// Marks a request type as requiring one or more authorization policies. The
/// named policy must be defined via <see cref="PolicyAttribute"/> somewhere
/// in the consumer's compilation or referenced assemblies. Stack the attribute
/// to require multiple policies (all must pass).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class RequirePolicyAttribute : Attribute
{
    public RequirePolicyAttribute(string policyName) => PolicyName = policyName;
    public string PolicyName { get; }
}
