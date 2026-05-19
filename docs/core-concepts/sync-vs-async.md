---
id: sync-vs-async
title: Sync vs Async
sidebar_position: 4
---

# Sync vs Async

There is no choice in v2 — `IAuthorizationPolicy` exposes a single async entry point, `EvaluateAsync`, and every policy implements it.

```csharp
public interface IAuthorizationPolicy
{
    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default);
}
```

The four-method v1 surface (`IsAuthorized`, `IsAuthorizedAsync`, `Evaluate`, `EvaluateAsync`) is gone. One method, async-shaped, structured-result return.

---

## Sync-completing policies

A CPU-bound check that does no I/O still implements `EvaluateAsync` — it just wraps the synchronous result in a completed `ValueTask`:

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

`new ValueTask<UnitResult<AuthorizationFailure>>(syncResult)` is the allocation-free wrap. Both `UnitResult<AuthorizationFailure>` and `AuthorizationFailure` are value types, so the whole expression lives on the stack — no `Task` heap allocation, no boxing.

---

## I/O-bound policies

When you genuinely need to `await`, mark the method `async` and let the compiler build the state machine:

```csharp
public async ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
    ISecurityContext ctx, CancellationToken ct = default)
{
    var active = await tenants.IsActiveAsync(ctx.Id, ct).ConfigureAwait(false);
    return active
        ? UnitResult<AuthorizationFailure>.Success()
        : new AuthorizationFailure("tenant.inactive", "tenant is suspended");
}
```

The first await on a non-completed task allocates the state machine — that cost lives in your I/O, not in the contract.

---

## Cancellation

`EvaluateAsync` accepts a `CancellationToken`. The host's dispatcher passes the request's token through; your override is responsible for honoring it during awaits — pass it into the underlying client (HTTP, DB) and don't swallow `OperationCanceledException`. For sync-completing policies it costs nothing to call `ct.ThrowIfCancellationRequested()` before the body if you want a pre-cancelled token to short-circuit.

```csharp
public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
    ISecurityContext ctx, CancellationToken ct = default)
{
    ct.ThrowIfCancellationRequested();
    return new(ctx.Roles.Contains("Admin")
        ? UnitResult<AuthorizationFailure>.Success()
        : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Admin role required"));
}
```

---

## See also

- [Policies](policies.md) — the single-method `EvaluateAsync` contract.
- [Failure shape](failure-shape.md) — `AuthorizationFailure` deny codes.
- [Security context](security-context.md) — what `ctx` carries.
