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

Targets `net8.0`, `net9.0`, `net10.0`. AOT-compatible. The package bundles a Roslyn source generator — no separate `*.Generator` install.

## Write your first policy

A policy is a class that implements `IAuthorizationPolicy` and is named with `[Policy("...")]`:

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

The string passed to `[Policy]` is the **registration name** — the generator uses it to map a `[RequirePolicy("...")]` reference back to this class.

The contract is async-only: every policy overrides a single `EvaluateAsync` method. Sync-completing policies wrap their result in `new ValueTask<...>(syncResult)` — the value-typed `UnitResult<AuthorizationFailure>` keeps the happy path allocation-free.

For I/O-bound checks (tenant lookup, external claims service), `await` inside `EvaluateAsync`:

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

## Attach `[RequirePolicy]` to a request type

Reference the policy by name on the request class or struct you want guarded. `[RequirePolicy]` targets **types only** — it cannot be placed on methods (`ZAUTH005` fires at compile time):

```csharp
[RequirePolicy("AdminOnly")]
public sealed record DeleteUserCommand(string UserId);
```

Stack the attribute to require multiple policies — `AllowMultiple = true`, and every policy must allow:

```csharp
[RequirePolicy("ActiveTenant")]
[RequirePolicy("AdminOnly")]
public sealed record PurgeTenantCommand(string TenantId);
```

## What happens at runtime

The bundled source generator inspects your compilation for `[Policy]`-decorated classes and `[RequirePolicy]`-decorated request types, then emits:

- One `AuthorizerFor<TRequest>` subclass per request type, calling the named policies in order.
- An `AddZeroAllocAuthorization()` extension method on `IServiceCollection` that registers every `[Policy]` class and every emitted `AuthorizerFor<T>` as scoped services.

Hosts wire DI once and resolve `AuthorizerFor<TRequest>` per dispatch:

```csharp
using ZeroAlloc.Authorization.Generated;

builder.Services.AddZeroAllocAuthorization();
```

Per request:

```csharp
var authorizer = sp.GetService<AuthorizerFor<DeleteUserCommand>>();
if (authorizer is not null)
{
    var result = await authorizer.EvaluateAsync(securityContext, ct);
    if (result.IsFailure)
    {
        // host translates result.Error.Code / Reason into HTTP 403 or equivalent
        return Forbid(result.Error);
    }
}
```

If no `AuthorizerFor<T>` is registered for a request type, the request has no policies — proceed.

## Existing hosts

- [AI.Sentinel](https://github.com/MarcelRoozekrans/AI.Sentinel) — tool-call authorization for `IChatClient`-based agents.
- `ZeroAlloc.Mediator.Authorization` v5 — request-handler authorization built on `AuthorizerFor<TRequest>` resolved from DI.

## Anonymous callers

When a host has no caller identity to attach, pass `AnonymousSecurityContext.Instance` — a singleton with `Id = "anonymous"`, no roles, no claims. Reference-equality with `Instance` is the cheapest way for a policy to reject anonymous callers:

```csharp
public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
    ISecurityContext ctx, CancellationToken ct = default)
    => new(!ReferenceEquals(ctx, AnonymousSecurityContext.Instance) && ctx.Roles.Contains("Admin")
        ? UnitResult<AuthorizationFailure>.Success()
        : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Admin role required"));
```

## Generator diagnostics

The generator emits five compile-time diagnostics to catch wiring mistakes before they hit production:

| ID | Fires when |
|---|---|
| `ZAUTH001` | `[RequirePolicy("X")]` references a policy name with no matching `[Policy("X")]` in this compilation or referenced assemblies. |
| `ZAUTH002` | Two `[Policy("X")]` declarations use the same name. |
| `ZAUTH003` | `[Policy]`-decorated class does not implement `IAuthorizationPolicy`. |
| `ZAUTH004` | `[Policy]`-decorated class is abstract or static (cannot be instantiated by DI). |
| `ZAUTH005` | `[RequirePolicy]` placed on a non-class/non-struct target (interface, delegate, primitive, method). |

## Next

- [Attributes](attributes.md) — full `[Policy]` / `[RequirePolicy]` reference.
- [Policies](core-concepts/policies.md) — async-only contract, structured deny information.
- [Host integration](guides/host-integration.md) — how the generator + DI extension fit together.
