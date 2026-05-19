---
id: writing-a-policy
title: Writing a Policy
sidebar_position: 1
---

# Writing a Policy

A policy is one class that implements `IAuthorizationPolicy`, decorated with `[Policy("Name")]` so the bundled source generator can find it. The interface is a single method — `EvaluateAsync` — and every policy returns a structured `UnitResult<AuthorizationFailure>`.

See also: [policies](../core-concepts/policies.md), [sync vs async](../core-concepts/sync-vs-async.md), [failure shape](../core-concepts/failure-shape.md), [attributes](../attributes.md).

---

## CPU-bound — return a completed `ValueTask`

A check that touches nothing but the context itself (role membership, claim lookup, simple predicate) is naturally synchronous. Wrap the result in `new ValueTask<...>(syncResult)` — the value-typed `UnitResult<AuthorizationFailure>` keeps the happy path allocation-free:

```csharp
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;

[Policy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx.Roles.Contains("Admin")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Admin role required"));
}
```

---

## I/O-bound — `async` + `await`

A check that fundamentally needs to await something (DB lookup, HTTP call, external claims service) marks `EvaluateAsync` as `async`:

```csharp
[Policy("ActiveTenant")]
public sealed class ActiveTenantPolicy(ITenantService tenants) : IAuthorizationPolicy
{
    public async ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
    {
        var active = await tenants.IsActiveAsync(ctx.Id, ct).ConfigureAwait(false);
        return active
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("tenant.inactive", "tenant is suspended");
    }
}
```

The first `await` on a non-completed task allocates the state machine — that cost lives in your I/O, not in the contract. The dispatch layer itself stays zero-allocation.

---

## Structured deny — emit a coded `AuthorizationFailure`

When a host needs to map deny reasons to API responses (HTTP status, log tag, telemetry counter), emit a specific `Code` instead of `DefaultDenyCode`:

```csharp
[Policy("AdminOnly")]
public sealed class RichDenyPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx.Roles.Contains("Admin")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("policy.deny.role", "user is not Admin"));
}
```

A host that reads `result.Error.Code == "policy.deny.role"` can translate it to a 403 with a structured body. See [failure shape](../core-concepts/failure-shape.md) for the full deny-code conventions.

---

## Decoration

The `[Policy("Name")]` attribute is what makes a class discoverable to the generator:

```csharp
[Policy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy { /* ... */ }
```

The string is the registration name — the generator uses it to map `[RequirePolicy("AdminOnly")]` references back to this class. One name per class, and derived classes do not inherit the registration; if a subclass is its own policy it must declare its own `[Policy("...")]`. See [attributes](../attributes.md) for the full property table.

A `[Policy]` class must implement `IAuthorizationPolicy` (`ZAUTH003` fires otherwise). It must be instantiable — abstract or static classes fire `ZAUTH004`. Two `[Policy("X")]` declarations with the same name fire `ZAUTH002`.

---

## Binding to a request

`[RequirePolicy]` lives on the request type (class or struct), not on the method:

```csharp
[RequirePolicy("AdminOnly")]
public sealed record DeleteUserCommand(string UserId);
```

Stack the attribute to require multiple policies (all must pass):

```csharp
[RequirePolicy("ActiveTenant")]
[RequirePolicy("AdminOnly")]
public sealed record PurgeTenantCommand(string TenantId);
```

Placing `[RequirePolicy]` on a method (or interface, delegate, primitive) fires `ZAUTH005` at compile time. The v2 model is one set of policy requirements per request, not per method.

---

## Anti-patterns to avoid

**Don't capture per-evaluation state on the policy class.** Policies are registered as scoped by default — but `[Policy]` classes can still be reused across awaits within a single request. Instance fields on the policy are shared across every concurrent caller into the scope — a `_lastUserId` field will race. Per-evaluation state belongs in `ISecurityContext` (or in a host-specific subinterface; see [security-context](../core-concepts/security-context.md)) or in injected per-request services.

**Don't `Task.Run` from inside `EvaluateAsync` to fake parallelism.** If you need to await I/O, just await it; the `ValueTask` shape handles sync-completing and async-completing paths uniformly. Offloading onto the thread pool allocates and blocks for no benefit.

**Don't return `Success()` for "policy doesn't apply."** Deny means deny; success means proceed. If a policy decides it has nothing to say about a particular call, that's the consumer's problem to solve by not attaching `[RequirePolicy]` to that request type. A policy that returns success whenever it doesn't apply turns into an accidental allow-list. The contract is binary: a policy's body answers "should this call proceed under this rule?" and nothing else.
