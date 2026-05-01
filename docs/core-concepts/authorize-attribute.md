---
id: authorize-attribute
title: Authorize Attribute
sidebar_position: 3
---

# Authorize Attribute

`[Authorize]` and `[AuthorizationPolicy]` form a two-attribute pair. Together they declare the binding between a guarded method and the policy class that decides — without the contract package itself doing any matching.

## Two attributes, two roles

```csharp
// On the policy class — declares "this class implements policy X".
[AuthorizationPolicy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
}

// On the consumer — declares "this method requires policy X".
public sealed class UserService
{
    [Authorize("AdminOnly")]
    public Task DeleteUserAsync(string userId) => Task.CompletedTask;
}
```

The `string` passed to each attribute is the registration name. The contract package stores it on the attribute and exposes it as `AuthorizationPolicyAttribute.Name` and `AuthorizeAttribute.PolicyName`. Nothing else.

---

## The host's job

At dispatch time the host:

1. Inspects the target (method, handler, tool) for `[Authorize]`.
2. Reads `PolicyName` off each attribute.
3. Looks up the matching `[AuthorizationPolicy]`-attributed class in its name → type registry.
4. Resolves the policy from DI.
5. Builds an `ISecurityContext` from the current request.
6. Invokes `IsAuthorized` / `IsAuthorizedAsync` / `EvaluateAsync`.
7. Allows or denies the call.

The contract package performs none of this. It carries the names; the host wires the dispatch.

---

## Multiple [Authorize] attributes

`AuthorizeAttribute` is `AllowMultiple = true`, so stacking is supported:

```csharp
public sealed class UserService
{
    [Authorize("ActiveTenant")]
    [Authorize("AdminOnly")]
    public Task PurgeTenantAsync(string tenantId) => Task.CompletedTask;
}
```

The contract is silent on combinator semantics — hosts decide whether multiple attributes mean "all must allow" (AND) or "any must allow" (OR). Existing hosts (AI.Sentinel, the planned Mediator.Authorization) treat the set as a conjunction: every policy must allow before the call dispatches.

> **Note:** explicit composition (e.g. `[Authorize("A", Mode = AuthorizeMode.Any)]`) is on the [backlog](../../backlog.md) and may land in a 1.x minor release. Until then, relying on the host's documented combinator is correct.

---

## Inheritance

| Attribute | `Inherited` | Reasoning |
|---|---|---|
| `[Authorize]` | `true` | A guard placed on a base class or virtual method should still apply to derived implementations. Removing the guard requires an explicit override. |
| `[AuthorizationPolicy]` | `false` | Policy registration is a property of a concrete class. A subclass that wants to be its own policy must declare its own `[AuthorizationPolicy("...")]`; otherwise the host would see two registrations claiming the same name. |

```csharp
public abstract class TenantService
{
    [Authorize("ActiveTenant")]
    public abstract Task ActOnTenantAsync(string tenantId);
}

public sealed class ConcreteTenantService : TenantService
{
    public override Task ActOnTenantAsync(string tenantId)  // [Authorize("ActiveTenant")] still applies
        => Task.CompletedTask;
}

[AuthorizationPolicy("ActiveTenant")]
public class ActiveTenantPolicyV1 : IAuthorizationPolicy { /* ... */ }

// Does NOT inherit the registration — must redeclare.
[AuthorizationPolicy("ActiveTenantStrict")]
public sealed class ActiveTenantPolicyV2 : ActiveTenantPolicyV1 { /* ... */ }
```

---

## Class-level placement

`[Authorize]` targets methods or classes. Class-level placement applies the policy to every method on the type, subject to host semantics:

```csharp
[Authorize("AdminOnly")]
public sealed class AdminController
{
    public Task DeleteUserAsync(string userId) => Task.CompletedTask;   // guarded by AdminOnly
    public Task ResetTenantAsync(string tenantId) => Task.CompletedTask; // guarded by AdminOnly
}
```

Whether method-level `[Authorize]` adds to or replaces class-level `[Authorize]` is a host decision. Most hosts merge the two sets and apply the conjunction.

---

## See also

- [Attributes reference](../attributes.md) — full property tables and discovery contract.
- [Policies](policies.md) — the `IAuthorizationPolicy` contract these attributes reference.
- [Getting started](../getting-started.md) — first end-to-end binding.
