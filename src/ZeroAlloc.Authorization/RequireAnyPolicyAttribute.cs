using System;

namespace ZeroAlloc.Authorization;

/// <summary>
/// Marks a request type as requiring at least ONE of the named authorization policies to pass.
/// Stacks with <see cref="RequirePolicyAttribute"/> and other <see cref="RequireAnyPolicyAttribute"/>
/// declarations (cross-attribute stacking is AND; within-attribute names form an OR group).
/// </summary>
/// <example>
/// <code>
/// [RequirePolicy("Admin")]
/// [RequireAnyPolicy("Premium", "Trusted")]
/// public sealed record ViewBillingQuery();
/// // Effective: Admin AND (Premium OR Trusted)
/// </code>
/// </example>
/// <remarks>
/// All listed policy names must be defined via <see cref="PolicyAttribute"/> in the consumer's
/// compilation or referenced assemblies. When all candidates fail at runtime, the generator
/// synthesises a combined <see cref="AuthorizationFailure"/> with <c>Code = "any.all_failed"</c>
/// and <c>Reason</c> listing each policy's individual failure (declaration order).
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class RequireAnyPolicyAttribute : Attribute
{
    public RequireAnyPolicyAttribute(params string[] policyNames) => PolicyNames = policyNames;
    public string[] PolicyNames { get; }
}
