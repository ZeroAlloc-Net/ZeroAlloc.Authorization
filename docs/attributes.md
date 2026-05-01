---
id: attributes
title: Attributes
sidebar_position: 3
---

# Attributes

Two attributes ship in the contract. Neither does anything on its own — both are read by hosts at composition time.

## [Authorize]

Binds a method or class to a named policy.

```csharp
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Class,
    AllowMultiple = true,
    Inherited = true)]
public sealed class AuthorizeAttribute : Attribute
{
    public AuthorizeAttribute(string policyName);
    public string PolicyName { get; }
}
```

| Property | Type | Description |
|---|---|---|
| `PolicyName` | `string` | Required. Non-null, non-empty. Must match an `[AuthorizationPolicy("...")]` `Name` registered with the host. |

Targets methods or classes. `AllowMultiple = true` — stacking `[Authorize]` declarations is supported, and hosts treat the set as a conjunction (every policy must allow). `Inherited = true` — derived classes inherit class-level placements.

```csharp
public sealed class UserService
{
    [Authorize("AdminOnly")]
    public Task DeleteUserAsync(string userId) { /* ... */ return Task.CompletedTask; }

    [Authorize("ActiveTenant")]
    [Authorize("AdminOnly")]
    public Task PurgeTenantAsync(string tenantId) { /* ... */ return Task.CompletedTask; }
}
```

Behavior is contract-only. The attribute carries no logic — a host reads `PolicyName`, resolves it to a policy class, and invokes `IsAuthorized` / `IsAuthorizedAsync` before dispatch.

## [AuthorizationPolicy]

Names a policy class so it can be referenced from `[Authorize]` and host-specific binding APIs.

```csharp
[AttributeUsage(
    AttributeTargets.Class,
    AllowMultiple = false,
    Inherited = false)]
public sealed class AuthorizationPolicyAttribute : Attribute
{
    public AuthorizationPolicyAttribute(string name);
    public string Name { get; }
}
```

| Property | Type | Description |
|---|---|---|
| `Name` | `string` | Required. Non-null, non-empty. The registration name hosts use to map `[Authorize("Name")]` to this class. |

Targets classes only. `AllowMultiple = false` — one registration name per class. `Inherited = false` — derived policy classes do not inherit the registration; if a derived class is its own policy it must declare its own `[AuthorizationPolicy("...")]`.

```csharp
[AuthorizationPolicy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
}
```

The class must implement `IAuthorizationPolicy`. The contract package does not enforce that constraint at compile time — hosts surface registration errors when they build their name→type registry.

## Discovery

Hosts scan `[AuthorizationPolicy]`-attributed types and build a `name → Type` registry, then resolve `[Authorize("Name")].PolicyName` against it at dispatch time. The contract package itself performs no scanning, no caching, and no registration — every discovery strategy (assembly walk, source generator, hand-registration) lives in the host.

See [Host integration](guides/host-integration.md) for the registry contract a host is expected to satisfy.
