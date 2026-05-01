---
id: sync-vs-async
title: Sync vs Async
sidebar_position: 4
---

# Sync vs Async

`IAuthorizationPolicy` exposes both sync and async entry points so policies can opt into the shape that matches their actual work. The defaults funnel everything to `IsAuthorized`, so opting in is purely additive.

## Decision matrix

| Your check is... | Override |
|---|---|
| Pure CPU — role / claim membership, simple predicates | `IsAuthorized` only. |
| I/O-bound — DB lookup, HTTP call, external claims | `IsAuthorizedAsync`. Throw from `IsAuthorized`. |
| Sync, but you want a coded deny reason | `Evaluate`, returning `UnitResult<AuthorizationFailure>`. |
| Both I/O-bound and structured deny info | `EvaluateAsync`. |

See [failure shape](failure-shape.md) for the structured-result types.

---

## Pure CPU

```csharp
[AuthorizationPolicy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
}
```

Hosts may dispatch via `IsAuthorized`, `IsAuthorizedAsync`, `Evaluate`, or `EvaluateAsync` — all four reach the same body. The async wrappers use `ValueTask.FromResult`, which does not heap-allocate.

## I/O-bound

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

Hosts that can dispatch async should call `IsAuthorizedAsync`. Hosts that cannot (rare; typically a constraint of the framework being guarded) should treat the `InvalidOperationException` as a deny, not as a fault. This is the convention codified in the README's `TenantPolicy` example.

## Structured deny

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

When a host calls `Evaluate` directly, it gets the coded deny. When it calls `IsAuthorized`, it still gets a `bool`. The two are kept in sync by the policy author — the contract does not enforce consistency.

---

## Performance

Every entry point is zero-allocation on the happy path. From the [README](../../README.md) benchmark table (BDN ShortRun, .NET 10 release, x64, simple role-check policy):

| Method | Mean | Allocated |
|---|---:|---:|
| `IsAuthorized` | ~9 ns | 0 B |
| `IsAuthorizedAsync` | ~31 ns | 0 B |
| `Evaluate` | ~7 ns | 0 B |
| `EvaluateAsync` | ~99 ns | 0 B |

`IsAuthorizedAsync` stays at 0 B because the default wrapper is `ValueTask.FromResult(IsAuthorized(ctx))`. `EvaluateAsync` stays at 0 B because `UnitResult<AuthorizationFailure>` is a value type and `AuthorizationFailure` is a `readonly struct`. Async overrides that hit real I/O will allocate the state machine on first await; that cost is unavoidable and lives in your tenant lookup, not in this contract.

---

## Cancellation

Both async entry points accept a `CancellationToken`:

```csharp
ValueTask<bool> IsAuthorizedAsync(ISecurityContext ctx, CancellationToken ct = default);
ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
    ISecurityContext ctx, CancellationToken ct = default);
```

The default implementations call `ct.ThrowIfCancellationRequested()` synchronously before invoking the underlying check, so a cancelled token short-circuits without dispatching. Once your override starts awaiting I/O, your code is responsible for honoring `ct` during the await — pass it into the underlying client (HTTP, DB) and don't swallow `OperationCanceledException`.

```csharp
public async ValueTask<bool> IsAuthorizedAsync(
    ISecurityContext ctx, CancellationToken ct = default)
{
    ct.ThrowIfCancellationRequested();
    return await tenants.IsActiveAsync(ctx.Id, ct).ConfigureAwait(false);
}
```

---

## See also

- [Policies](policies.md) — the four-method contract these examples implement.
- [Failure shape](failure-shape.md) — `AuthorizationFailure` deny codes.
- [Security context](security-context.md) — what `ctx` carries.
