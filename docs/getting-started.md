---
id: getting-started
title: Getting Started
sidebar_position: 2
---

# Getting Started

## Install

```bash
dotnet add package ZeroAlloc.Authorization
```

Targets `net8.0`, `net9.0`, `net10.0`. AOT-compatible.

> **Note:** in ASP.NET Core projects, `using ZeroAlloc.Authorization;` collides with `using Microsoft.AspNetCore.Authorization;` over the `[Authorize]` name. Use a `using` alias (`using ZAuthorize = ZeroAlloc.Authorization;`) or fully-qualify one side at the call site.

## Write your first policy

A policy is a class that implements `IAuthorizationPolicy` and is named with `[AuthorizationPolicy("...")]`:

```csharp
using ZeroAlloc.Authorization;

[AuthorizationPolicy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
}
```

The string passed to `[AuthorizationPolicy]` is the **registration name** — hosts use it to map an `[Authorize]` reference back to this class.

For I/O-bound checks (tenant lookup, external claims service), override `IsAuthorizedAsync` instead and let the sync method throw `InvalidOperationException` — hosts that cannot dispatch asynchronously will treat the throw as a deny:

```csharp
[AuthorizationPolicy("ActiveTenant")]
public sealed class ActiveTenantPolicy(ITenantService tenants) : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) =>
        throw new InvalidOperationException("Use async — tenant lookup is I/O-bound.");

    public async ValueTask<bool> IsAuthorizedAsync(ISecurityContext ctx, CancellationToken ct = default)
        => await tenants.IsActiveAsync(ctx.Id, ct).ConfigureAwait(false);
}
```

## Attach `[Authorize]` to a method

Reference the policy by name on the method (or class) you want guarded:

```csharp
public sealed class UserService
{
    [Authorize("AdminOnly")]
    public Task DeleteUserAsync(string userId)
    {
        // ...
        return Task.CompletedTask;
    }

    [Authorize("ActiveTenant")]
    [Authorize("AdminOnly")]
    public Task PurgeTenantAsync(string tenantId)
    {
        // Multiple [Authorize] attributes apply — host evaluates them all and
        // requires every policy to allow before dispatch.
        return Task.CompletedTask;
    }
}
```

`[Authorize]` is `AllowMultiple = true` and targets methods or classes — class-level placement applies the policy to every method on the type, subject to host semantics.

## What happens at runtime

Nothing — until a host runs. This package ships only the contract; it has no dispatcher, no DI registration, no scanner. A host inspects `[Authorize]` on the dispatch target, looks up the matching `[AuthorizationPolicy]` class in its registry, builds an `ISecurityContext` from the current request, and calls `IsAuthorized` / `IsAuthorizedAsync`. If the policy denies, the host short-circuits before invoking the user code.

See [Host integration](guides/host-integration.md) for the full wiring contract a host must satisfy.

## Existing hosts

- [AI.Sentinel](https://github.com/MarcelRoozekrans/AI.Sentinel) — tool-call authorization for `IChatClient`-based agents.
- `ZeroAlloc.Mediator.Authorization` (planned) — request-handler authorization.

## Anonymous callers

When a host has no caller identity to attach, it should pass `AnonymousSecurityContext.Instance` — a singleton with `Id = "anonymous"`, no roles, no claims. Reference-equality with `Instance` is the cheapest way for a policy to reject anonymous callers:

```csharp
public bool IsAuthorized(ISecurityContext ctx)
    => !ReferenceEquals(ctx, AnonymousSecurityContext.Instance) && ctx.Roles.Contains("Admin");
```

## Next

- [Attributes](attributes.md) — full `[Authorize]` / `[AuthorizationPolicy]` reference.
- [Policies](core-concepts/policies.md) — sync vs async, structured `Evaluate` failures.
- [Host integration](guides/host-integration.md) — write your own dispatcher.
