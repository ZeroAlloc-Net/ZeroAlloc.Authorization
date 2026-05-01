---
id: index
title: ZeroAlloc.Authorization
sidebar_position: 1
---

# ZeroAlloc.Authorization

Authorization primitives for .NET. Five types — `ISecurityContext`, `IAuthorizationPolicy`, `[Authorize]`, `[AuthorizationPolicy]`, `AnonymousSecurityContext` — designed to be shared across hosts that need a unified policy contract.

## Quick example

```csharp
[AuthorizationPolicy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
}

public sealed class UserService
{
    [Authorize("AdminOnly")]
    public Task DeleteUserAsync(string userId) { /* ... */ }
}
```

The contract package stops here. A host matches the `[Authorize]` policy name to the registered `[AuthorizationPolicy]` class and invokes `IsAuthorized` / `IsAuthorizedAsync` before dispatch.

---

## What it isn't

This is a **contract** package, not a host. It ships interfaces and attributes — nothing else.

- No dispatcher. Nothing calls `IsAuthorized` for you.
- No DI registration. There is no `AddZeroAllocAuthorization()` extension.
- No integration with ASP.NET Core, MVC, minimal APIs, or any specific framework.
- No attribute scanner. Hosts walk the `[AuthorizationPolicy]`-attributed types themselves.

Hosts provide all of that:

- [AI.Sentinel](https://github.com/MarcelRoozekrans/AI.Sentinel) — tool-call authorization for `IChatClient`-based agents.
- `ZeroAlloc.Mediator.Authorization` (planned) — request-handler authorization.

If you need an integration that does not exist yet, write a host. The contract is small on purpose.

---

## Quick links

- [Getting started](getting-started.md) — install, write your first policy, attach `[Authorize]`.
- [Policies](core-concepts/policies.md) — `IAuthorizationPolicy`, sync vs async, structured `Evaluate` results.
- [Security context](core-concepts/security-context.md) — `ISecurityContext`, host-specific subinterfaces, the anonymous singleton.
- [Attributes](attributes.md) — `[Authorize]` and `[AuthorizationPolicy]` reference.
- [Host integration](guides/host-integration.md) — how to wire a host to the contract.

---

## Targets

`net8.0`, `net9.0`, `net10.0`. AOT-compatible — `<IsAotCompatible>true</IsAotCompatible>` is set on the main library and the `samples/ZeroAlloc.Authorization.AotSmoke/` app is exercised on every CI run with `PublishAot=true`.

> **Note:** in ASP.NET Core projects, `using ZeroAlloc.Authorization;` collides with `using Microsoft.AspNetCore.Authorization;` over the `[Authorize]` name. Use a `using` alias (`using ZAuthorize = ZeroAlloc.Authorization;`) or fully-qualify one side at the call site.
