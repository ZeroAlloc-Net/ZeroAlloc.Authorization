using System;

namespace ZeroAlloc.Authorization;

/// <summary>
/// Declares an authorization policy. The decorated class must implement
/// <see cref="IAuthorizationPolicy"/> and be reachable from the consumer's
/// compilation or referenced assemblies. The source generator emits a DI
/// registration for each <c>[Policy]</c>-decorated class.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PolicyAttribute : Attribute
{
    public PolicyAttribute(string name) => Name = name;
    public string Name { get; }
}
