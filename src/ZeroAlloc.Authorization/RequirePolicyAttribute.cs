using System;

namespace ZeroAlloc.Authorization;

/// <summary>
/// Marks a request type as requiring one or more authorization policies. The
/// named policy must be defined via <see cref="PolicyAttribute"/> somewhere
/// in the consumer's compilation or referenced assemblies. Stack the attribute
/// to require multiple policies (all must pass).
/// </summary>
/// <remarks>
/// Pass compile-time-constant args via the <c>params object?[]</c> overload to invoke a
/// generic <see cref="IAuthorizationPolicy{T1}"/> / <see cref="IAuthorizationPolicy{T1, T2}"/> /
/// <see cref="IAuthorizationPolicy{T1, T2, T3}"/> policy. The generator validates the arg
/// shape against the policy's declared interface at compile time (see <c>ZAUTH007</c>).
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class RequirePolicyAttribute : Attribute
{
    /// <summary>Parameterless overload — selects the parameterless <see cref="IAuthorizationPolicy"/>.</summary>
    public RequirePolicyAttribute(string policyName) => (PolicyName, PolicyArgs) = (policyName, null);

    /// <summary>Parameterized overload — selects a generic <see cref="IAuthorizationPolicy{T1}"/>-family policy.</summary>
    /// <param name="args">Compile-time-constant arguments forwarded to the policy's <c>EvaluateAsync</c>.</param>
    public RequirePolicyAttribute(string policyName, params object?[] args)
        => (PolicyName, PolicyArgs) = (policyName, args);

    public string PolicyName { get; }
    public object?[]? PolicyArgs { get; }
}
