---
id: policies
title: Policies
sidebar_position: 2
---

# Policies

`IAuthorizationPolicy` is the rule a host evaluates before dispatch. The interface exposes four entry points; one is required, three are default-implemented overrides for richer scenarios.

```csharp
public interface IAuthorizationPolicy
{
    bool IsAuthorized(ISecurityContext ctx);

    ValueTask<bool> IsAuthorizedAsync(ISecurityContext ctx, CancellationToken ct = default)
        => ValueTask.FromResult(IsAuthorized(ctx));

    UnitResult<AuthorizationFailure> Evaluate(ISecurityContext ctx)
        => IsAuthorized(ctx)
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode);

    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default);
}
```

`IsAuthorized` is the only method without a default implementation — every policy must provide it. Override the others to opt into async dispatch or structured deny information. See [sync vs async](sync-vs-async.md) and [failure shape](failure-shape.md) for the longer story.

---

## When to override which

| Scenario | Override |
|---|---|
| Pure-CPU check, no I/O | `IsAuthorized` only — every other method delegates to it. |
| I/O-bound check (DB, HTTP, external claims) | `IsAuthorizedAsync`. Throw from `IsAuthorized` if the host calls it. |
| Sync check that wants a richer deny code/reason | `Evaluate`, returning `UnitResult<AuthorizationFailure>`. |
| Both I/O-bound and structured deny info | `EvaluateAsync`. |

The default implementations of `IsAuthorizedAsync`, `Evaluate`, and `EvaluateAsync` all funnel back to `IsAuthorized`. If your check is purely synchronous, just override `IsAuthorized` and let the rest fall through — you stay zero-allocation on every entry point because `ValueTask.FromResult` does not heap-allocate and the structured-result wrapper unboxes a `UnitResult<AuthorizationFailure>` value.

---

## Sync-only example

```csharp
[AuthorizationPolicy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
}
```

Any host can dispatch this synchronously or via the async wrapper — the default `IsAuthorizedAsync` returns `ValueTask.FromResult(IsAuthorized(ctx))` with no allocation.

## I/O-bound example

For checks that fundamentally cannot run synchronously (tenant validation, external claims service), override `IsAuthorizedAsync` and have `IsAuthorized` throw `InvalidOperationException`. Hosts that can dispatch async will call the async overload; hosts that cannot should treat the throw as a deny, not as a fault:

```csharp
[AuthorizationPolicy("ActiveTenant")]
public sealed class ActiveTenantPolicy(ITenantService tenants) : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) =>
        throw new InvalidOperationException("Use async — tenant lookup is I/O-bound.");

    public async ValueTask<bool> IsAuthorizedAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => await tenants.IsActiveAsync(ctx.Id, ct).ConfigureAwait(false);
}
```

This pattern — sync throws, async does the work — is the convention used by the README's `TenantPolicy` example and by the planned Mediator.Authorization handlers.

## Structured-deny example

When a host wants to map deny reasons to API responses, return a richer `UnitResult<AuthorizationFailure>` instead of a `bool`:

```csharp
[AuthorizationPolicy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");

    public UnitResult<AuthorizationFailure> Evaluate(ISecurityContext ctx)
        => ctx.Roles.Contains("Admin")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("policy.deny.role", "user is not Admin");
}
```

See [failure shape](failure-shape.md) for the full deny-code conventions.

---

## Lifetime and DI

The contract package does not register policies. Hosts do — they walk `[AuthorizationPolicy]`-attributed types (see [attributes](../attributes.md)) and add them to their own DI container. Each host decides the scope:

- **Singleton** — pure-CPU policies with no per-request state.
- **Scoped** — policies that depend on scoped services (DbContext, current-tenant accessor).
- **Transient** — rarely needed; useful when a policy holds short-lived state across a single evaluation.

A host that supports DI typically resolves the policy from the container at dispatch time, after looking up the registered type by name. The contract is type-only — no construction, no scoping decisions live here.

---

## See also

- [Sync vs async](sync-vs-async.md) — when each evaluation entry point matters.
- [Failure shape](failure-shape.md) — `AuthorizationFailure` and `UnitResult<AuthorizationFailure>`.
- [Authorize attribute](authorize-attribute.md) — how a policy is bound to a method.
- [Security context](security-context.md) — what flows into `IsAuthorized`.
