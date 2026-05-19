---
id: policies
title: Policies
sidebar_position: 2
---

# Policies

`IAuthorizationPolicy` is the rule a host evaluates before dispatch. v2 collapsed the interface to a **single async method** — every policy implements `EvaluateAsync` and nothing else.

```csharp
public interface IAuthorizationPolicy
{
    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default);
}
```

That's the whole contract. No sync vs async choice, no four-method matrix, no boolean shortcut. Every entry point returns a `UnitResult<AuthorizationFailure>` so hosts always have a machine-readable code on deny.

---

## Sync-completing policies

A check that touches nothing but the context itself (role membership, claim lookup, simple predicate) is naturally synchronous. Return the result as a completed `ValueTask` — both `UnitResult<AuthorizationFailure>` and `AuthorizationFailure` are value types, so the entire happy path stays on the stack:

```csharp
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

The `new(...)` constructor wraps the synchronous result in a `ValueTask<UnitResult<AuthorizationFailure>>` without allocating a `Task`. This is the idiom for every CPU-bound policy.

---

## I/O-bound policies

A check that genuinely needs to await something (DB lookup, HTTP call, external claims service) marks `EvaluateAsync` as `async` and awaits inside:

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

The first `await` of a non-completed task allocates the state machine — that cost lives in your I/O, not in the dispatcher. The dispatch layer itself stays zero-allocation.

---

## Structured deny

`AuthorizationFailure` carries a machine-readable `Code` and an optional human-readable `Reason`. Use specific codes for cases the host needs to distinguish:

```csharp
return new AuthorizationFailure("policy.deny.role", "user is not Admin");
return new AuthorizationFailure("tenant.inactive", "tenant is suspended");
return new AuthorizationFailure("scope.missing", "token lacks 'admin:write'");
```

See [failure shape](failure-shape.md) for the full deny-code conventions.

---

## Lifetime and DI

The bundled source generator registers every `[Policy]`-decorated class **as scoped** via the emitted `AddZeroAllocAuthorization()` extension. Scoped fits the common case: policies can depend on scoped services (DbContext, current-tenant accessor, the HTTP request's claims principal) without lifetime mismatches.

If you need a different lifetime for a specific policy — singleton for a pure-CPU rule, transient for short-lived state — register it explicitly **after** calling `AddZeroAllocAuthorization()` so your registration wins:

```csharp
services.AddZeroAllocAuthorization();
services.AddSingleton<AdminOnlyPolicy>(); // overrides the generator's scoped registration
```

---

## See also

- [Sync vs async](sync-vs-async.md) — the async-only contract note and the `new ValueTask<...>(syncResult)` idiom.
- [Failure shape](failure-shape.md) — `AuthorizationFailure` and `UnitResult<AuthorizationFailure>`.
- [RequirePolicy attribute](require-policy-attribute.md) — how a policy is bound to a request type.
- [Security context](security-context.md) — what flows into `EvaluateAsync`.
