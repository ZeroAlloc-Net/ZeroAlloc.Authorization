---
id: writing-a-policy
title: Writing a Policy
sidebar_position: 1
---

# Writing a Policy

A policy is one class that implements `IAuthorizationPolicy`, decorated with `[AuthorizationPolicy("Name")]` so a host can find it. There are three shapes — pure-CPU, I/O-bound, structured-deny — and one decoration rule. This guide walks each shape end-to-end.

See also: [policies](../core-concepts/policies.md), [sync vs async](../core-concepts/sync-vs-async.md), [failure shape](../core-concepts/failure-shape.md), [attributes](../attributes.md).

---

## Pure CPU — override `IsAuthorized` only

A check that touches nothing but the context itself (role membership, claim lookup, simple predicate) overrides `IsAuthorized` and lets every other entry point fall through to it:

```csharp
using ZeroAlloc.Authorization;

[AuthorizationPolicy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
}
```

The default `IsAuthorizedAsync` wraps this in `ValueTask.FromResult` (no allocation). The default `Evaluate` returns `UnitResult<AuthorizationFailure>.Success()` or a `default` failure with `Code == "policy.deny"`. Hosts that dispatch via any of the four entry points reach the same body.

---

## I/O-bound — override `IsAuthorizedAsync`

A check that fundamentally cannot run synchronously (DB lookup, HTTP call, external claims service) overrides the async overload and throws from the sync path:

```csharp
[AuthorizationPolicy("ActiveTenant")]
public sealed class TenantPolicy(ITenantService tenants) : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) =>
        throw new InvalidOperationException("Use async — tenant lookup is I/O-bound.");

    public async ValueTask<bool> IsAuthorizedAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => await tenants.IsActiveAsync(ctx.Id, ct).ConfigureAwait(false);
}
```

The throw is the explicit signal: a host that called the sync path either has the wrong dispatcher, or it should treat the throw as a deny. Either way, the policy author surfaces the constraint loudly rather than fabricating a fake answer.

---

## Structured deny — override `Evaluate`

When a host needs to map deny reasons to API responses (HTTP status, log tag, telemetry counter), override `Evaluate` and return a coded `AuthorizationFailure` instead of a bare `bool`:

```csharp
[AuthorizationPolicy("AdminOnly")]
public sealed class RichDenyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");

    public UnitResult<AuthorizationFailure> Evaluate(ISecurityContext ctx)
        => ctx.Roles.Contains("Admin")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("policy.deny.role", "user is not Admin");
}
```

A host that calls `Evaluate` reads `result.Error.Code == "policy.deny.role"` and translates it. A host that only calls `IsAuthorized` still gets a correct deny — just without the code/reason. See [failure shape](../core-concepts/failure-shape.md) for the full deny-code conventions.

For a check that is both I/O-bound and emits coded denies, override `EvaluateAsync` directly.

---

## Decoration

The `[AuthorizationPolicy("Name")]` attribute is what makes a class discoverable to a host:

```csharp
[AuthorizationPolicy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy { /* ... */ }
```

The string is the registration name — hosts use it to map `[Authorize("AdminOnly")]` references back to this class. One name per class, and derived classes do not inherit the registration; if a subclass is its own policy it must declare its own `[AuthorizationPolicy("...")]`. See [attributes](../attributes.md) for the full property table.

---

## Anti-patterns to avoid

**Don't capture per-evaluation state on the policy class.** Most hosts register policies as singletons. Instance fields on the policy are shared across every concurrent evaluation — a `_lastUserId` field will race. Per-evaluation state belongs in `ISecurityContext` (or in a host-specific subinterface; see [security-context](../core-concepts/security-context.md)) or in injected per-request services. Constructor-injected dependencies are fine if the host registers them with the right lifetime.

**Don't `Task.Run` from a sync `IsAuthorized` to fake async.** Offloading I/O onto a thread-pool thread strictly worsens the situation: it allocates a `Task`, blocks one pool thread, and still synchronously waits. If the check is genuinely I/O-bound, throw from `IsAuthorized` and override `IsAuthorizedAsync`. Let the host call the right overload (or treat the throw as a deny if it cannot).

**Don't return `false` for "policy doesn't apply."** Deny means deny. If a policy decides it has nothing to say about a particular call, that's the host's problem to solve at registration time — the host should select a different policy, or none at all. A policy that returns `true` whenever it doesn't apply turns into an accidental allow-list; a policy that returns `false` when it doesn't apply blocks unrelated calls. Both are bugs. The contract is binary: a policy's body answers "should this call proceed under this rule?" and nothing else.
