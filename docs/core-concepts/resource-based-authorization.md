---
id: resource-based-authorization
title: Resource-Based Authorization
sidebar_position: 8
---

# Resource-Based Authorization

A subset of authorization rules need to look at **the thing being acted upon**
— "the post being edited," "the file being deleted." Each host already has a
subinterface that carries the request (see
[Security context](security-context.md) for the
`IToolCallSecurityContext` / `IRequestSecurityContext<TRequest>` patterns), but
those subinterfaces are host-specific. A policy class written against
`IRequestSecurityContext<EditPostCommand>` cannot be reused inside
`AI.Sentinel`, even when both hosts dispatch the same underlying resource type.

v2.1 ships a **resource-typed** context contract so a single
`OwnerOnlyPolicy` can serve any host whose dispatch context carries the same
resource shape.

---

## IResourceSecurityContext&lt;TResource&gt;

```csharp
public interface IResourceSecurityContext<out TResource> : ISecurityContext
{
    TResource Resource { get; }
}
```

The interface is **covariant** in `TResource` — an
`IResourceSecurityContext<Post>` is assignable to
`IResourceSecurityContext<object>`. Hosts implement it on the same context
type they already populate; policies type-check via pattern match:

```csharp
if (ctx is IResourceSecurityContext<Post> rc)
{
    var post = rc.Resource;
    // ... resource-typed decision ...
}
```

The pattern-match is the seam. A policy that wants a `Post` checks for
`IResourceSecurityContext<Post>`; a policy that wants a `File` checks for
`IResourceSecurityContext<File>`. The same `OwnerOnlyPolicy` class — keyed on
its resource generic parameter — can answer in both hosts when each populates
the matching resource type.

---

## Worked example: OwnerOnlyPolicy

```csharp
[Policy("OwnerOnly")]
public sealed class OwnerOnlyPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx is IResourceSecurityContext<Post> rc && rc.Resource.OwnerId == ctx.Id
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("resource.not_owner"));
}

public sealed record Post(string Id, string OwnerId);

[RequirePolicy("OwnerOnly")]
public sealed record EditPostCommand(string PostId, string NewBody);
```

The policy stays on the **parameterless** `IAuthorizationPolicy` contract —
the resource is read through the context, not through `[RequirePolicy]` args.
That keeps `[RequirePolicy("OwnerOnly")]` declaration-free at the request
site.

---

## The dormant-contract semantic

v2.1 ships **the interface only**. Host packages —
`ZeroAlloc.Mediator.Authorization` and `AI.Sentinel` — adopt by populating
the typed-resource context inside their dispatch behaviour as a follow-up.
Until those hosts ship resource-aware contexts:

- `ctx is IResourceSecurityContext<T>` **falls through to `false`** for every
  built-in context (`AnonymousSecurityContext`, host plain-context types).
- A policy that depends on the resource branch should **fail closed** — exactly
  as it would if a host were missing any other expected context field.

`OwnerOnlyPolicy` above is fail-closed by construction: the resource branch
is the only success path, so a context that does not implement
`IResourceSecurityContext<Post>` denies with `"resource.not_owner"`. Avoid
the inverted shape — "deny only if the cast succeeds and the owner mismatches"
— because it allows every non-resource-aware host to bypass the rule silently.

---

## What follow-up adoption looks like

Once a host populates the resource:

```csharp
// Host-side (illustrative — actual wiring lives in the host package).
public sealed class MediatorAuthContext<TRequest>(string id, TRequest request, ...)
    : ISecurityContext, IResourceSecurityContext<TRequest>
{
    public string Id => id;
    public TRequest Resource => request;
    // ... Roles, Claims, host-specific fields ...
}
```

No change to the policy. The same `OwnerOnlyPolicy` class begins to allow
real callers as soon as the host's context implements
`IResourceSecurityContext<Post>`.

---

## See also

- [Security context](security-context.md) — the base `ISecurityContext`
  contract and host subinterface pattern.
- [Policies](policies.md) — `IAuthorizationPolicy` and the deny-shape
  conventions.
- [Failure shape](failure-shape.md) — `AuthorizationFailure.Code` conventions
  for resource-typed denies.
