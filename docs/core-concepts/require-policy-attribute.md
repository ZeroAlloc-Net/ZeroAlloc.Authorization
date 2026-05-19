---
id: require-policy-attribute
title: RequirePolicy Attribute
sidebar_position: 3
---

# RequirePolicy Attribute

`[RequirePolicy]` and `[Policy]` form a two-attribute pair. Together they declare the binding between a guarded request type and the policy class that decides — without the consumer ever writing matching code, because the bundled source generator does it at compile time.

## Two attributes, two roles

```csharp
// On the policy class — declares "this class implements policy X".
[Policy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx.Roles.Contains("Admin")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Admin role required"));
}

// On the request — declares "this request requires policy X".
[RequirePolicy("AdminOnly")]
public sealed record DeleteUserCommand(string UserId);
```

The `string` passed to each attribute is the registration name. The contract package stores it on the attribute and exposes it as `PolicyAttribute.Name` and `RequirePolicyAttribute.PolicyName`. The generator does the matching.

---

## What the generator does

At compile time the generator:

1. Walks `[Policy]`-attributed classes across the compilation and referenced assemblies, building a `name → type` table.
2. Walks `[RequirePolicy]`-attributed request types, collecting the policy names each requires.
3. Validates the pairing — unknown policy names emit `ZAUTH001`, duplicates emit `ZAUTH002`, etc.
4. Emits one `AuthorizerFor<TRequest>` subclass per `[RequirePolicy]`-decorated type. The subclass evaluates the named policies in order and returns the first failure (conjunction).
5. Emits an `AddZeroAllocAuthorization()` extension on `IServiceCollection` that registers every `[Policy]` class and every emitted `AuthorizerFor<T>` as scoped.

At dispatch time the host:

1. Builds an `ISecurityContext` from the current request.
2. Resolves `AuthorizerFor<TRequest>` from DI.
3. Calls `EvaluateAsync(ctx, ct)` and translates the result.

Neither the host nor the consumer writes lookup code — the generator owns the seam.

---

## Multiple [RequirePolicy] attributes

`RequirePolicyAttribute` is `AllowMultiple = true`, so stacking is supported:

```csharp
[RequirePolicy("ActiveTenant")]
[RequirePolicy("AdminOnly")]
public sealed record PurgeTenantCommand(string TenantId);
```

The generator emits a dispatcher that evaluates the policies in declaration order and returns the first failure — every policy must allow before dispatch proceeds. This is a contract guarantee, not host policy.

---

## Inheritance

| Attribute | `Inherited` | Reasoning |
|---|---|---|
| `[RequirePolicy]` | `false` | Each request type declares its own guards explicitly. Inheritance would silently extend the policy set across derived requests — surprising and easy to miss in review. |
| `[Policy]` | `false` | Policy registration is a property of a concrete class. A subclass that wants to be its own policy must declare its own `[Policy("...")]`; otherwise the generator would see two registrations claiming the same name (`ZAUTH002`). |

---

## Class- and struct-level placement only

`[RequirePolicy]` targets `AttributeTargets.Class | AttributeTargets.Struct`. Method-level placement is no longer supported — the generator emits `ZAUTH005` at compile time if `[RequirePolicy]` is found on a method, interface, delegate, or primitive target.

If you previously placed `[Authorize]` on a method, hoist it onto the containing request type. The v2 model is one set of policy requirements per request, not per method.

---

## See also

- [Attributes reference](../attributes.md) — full property tables and generator-diagnostic reference.
- [Policies](policies.md) — the `IAuthorizationPolicy` contract these attributes reference.
- [Getting started](../getting-started.md) — first end-to-end binding.
