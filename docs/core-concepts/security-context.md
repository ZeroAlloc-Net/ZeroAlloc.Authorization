---
id: security-context
title: Security Context
sidebar_position: 1
---

# Security Context

`ISecurityContext` is the caller identity that flows into every authorization decision. It carries three fields and nothing else — hosts that need richer payloads add their own subinterface and policies downcast to read it.

```csharp
public interface ISecurityContext
{
    string Id { get; }
    IReadOnlySet<string> Roles { get; }
    IReadOnlyDictionary<string, string> Claims { get; }
}
```

---

## The three fields

| Member | Type | Convention |
|---|---|---|
| `Id` | `string` | Stable caller identifier — user, agent, or service name. Non-null, non-empty, stable for the lifetime of this context object. |
| `Roles` | `IReadOnlySet<string>` | Role membership of the caller. Empty (not null) for anonymous callers. |
| `Claims` | `IReadOnlyDictionary<string, string>` | Optional claims (tenant, scope, sub, etc.). Keys are case-sensitive `StringComparer.Ordinal` by convention. Values are strings — structured values (arrays, objects) should be JSON-encoded by the host that populates the dictionary, and decoded by the policy that consumes them. |

`Id` is whatever the host considers stable for the duration of the request — a user id, an API-key identifier, a service principal name. Policies should not assume it round-trips back to a user record; treat it as opaque unless your host documents otherwise.

---

## Host extension via subinterfaces

The contract intentionally stops at three fields. When a host needs to pass more — a tool name, a request payload, an HTTP path — it defines a subinterface that extends `ISecurityContext`, and policies downcast inside the body:

```csharp
// AI.Sentinel pattern: tool-call host
public interface IToolCallSecurityContext : ISecurityContext
{
    string ToolName { get; }
    IReadOnlyDictionary<string, object?> Args { get; }
}

// Planned Mediator.Authorization pattern: request-scoped host
public interface IRequestSecurityContext<TRequest> : ISecurityContext
{
    TRequest Request { get; }
}
```

A policy that wants to inspect those fields downcasts:

```csharp
[AuthorizationPolicy("NoDestructiveTools")]
public sealed class NoDestructiveToolsPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx)
    {
        if (ctx is IToolCallSecurityContext tc)
            return tc.ToolName != "delete_database";

        // Generic ISecurityContext — nothing tool-specific to check.
        return true;
    }
}
```

A policy that requires the host context — for example, one that only makes sense in a Mediator request — should fail closed when the downcast misses, rather than silently allow.

---

## The anonymous singleton

When a host has no caller identity to attach (unauthenticated request, background work without an impersonation), it should pass `AnonymousSecurityContext.Instance`:

```csharp
public sealed class AnonymousSecurityContext : ISecurityContext
{
    public const string AnonymousId = "anonymous";
    public static readonly AnonymousSecurityContext Instance = new();

    public string Id => AnonymousId;
    public IReadOnlySet<string> Roles { get; } = FrozenSet<string>.Empty;
    public IReadOnlyDictionary<string, string> Claims { get; } = FrozenDictionary<string, string>.Empty;
}
```

Reference-equality with `Instance` is the cheapest way for a policy to reject anonymous callers:

```csharp
public bool IsAuthorized(ISecurityContext ctx)
    => !ReferenceEquals(ctx, AnonymousSecurityContext.Instance)
       && ctx.Roles.Contains("Admin");
```

The `Roles` and `Claims` collections on the singleton are `FrozenSet<string>.Empty` and `FrozenDictionary<string, string>.Empty` — shared, allocation-free.

> **Note:** `AnonymousId` is a `public const string` so hosts can match against `ctx.Id == AnonymousSecurityContext.AnonymousId` if reference equality is not available (e.g. after serialization). Prefer the `ReferenceEquals(ctx, Instance)` form when you control the dispatcher.

---

## See also

- [Policies](policies.md) — how `IAuthorizationPolicy` consumes a context.
- [Authorize attribute](authorize-attribute.md) — how policy bindings are declared.
- [Getting started](../getting-started.md) — end-to-end first policy.
