---
id: index
title: ZeroAlloc.Authorization
sidebar_position: 1
---

# ZeroAlloc.Authorization

Authorization primitives for .NET — `ISecurityContext`, `IAuthorizationPolicy`, `[Policy]`, `[RequirePolicy]`, `AnonymousSecurityContext`, and an `AuthorizerFor<TRequest>` dispatcher emitted by the bundled source generator. Designed to be shared across hosts that need a unified policy contract.

## Quick example

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

[RequirePolicy("AdminOnly")]
public sealed record DeleteUserCommand(string UserId);
```

Host wiring is a single line:

```csharp
using ZeroAlloc.Authorization.Generated;
builder.Services.AddZeroAllocAuthorization();
```

Per dispatch, the host resolves `AuthorizerFor<TRequest>` from DI and calls `EvaluateAsync(ctx, ct)`. The generator has already discovered every `[Policy]` class and every `[RequirePolicy]`-decorated request, emitted the dispatcher subclasses, and registered them as scoped services.

---

## What ships

The package is the full stack — contract types **plus** a bundled Roslyn generator:

- Five contract types: `ISecurityContext`, `IAuthorizationPolicy`, `[Policy]`, `[RequirePolicy]`, `AnonymousSecurityContext`.
- One dispatcher base: `AuthorizerFor<TRequest>` (abstract; the generator emits concrete subclasses).
- One generator-emitted DI extension: `services.AddZeroAllocAuthorization()`.
- Eight compile-time diagnostics: `ZAUTH001`–`ZAUTH008`.
- v2.1 extensions: `[RequireAnyPolicy]` (OR composition),
  `IAuthorizationPolicy<T1...>` (parameterized policies, arity 1–3), and
  `IResourceSecurityContext<TResource>` (resource-based authorization,
  shipped dormant for host adoption).

Hosts (AI.Sentinel, `ZeroAlloc.Mediator.Authorization` v5, your own dispatcher) build a per-request `ISecurityContext`, resolve `AuthorizerFor<TRequest>` from the DI container, call `EvaluateAsync`, and translate the resulting `UnitResult<AuthorizationFailure>` into their outcome shape (HTTP 403, typed exception, tool-call refusal).

Existing hosts:

- [AI.Sentinel](https://github.com/MarcelRoozekrans/AI.Sentinel) — tool-call authorization for `IChatClient`-based agents.
- `ZeroAlloc.Mediator.Authorization` v5 — request-handler authorization.

If you need an integration that does not exist yet, write a host. The dispatch surface is a single method call.

---

## Quick links

- [Getting started](getting-started.md) — install, write your first policy, attach `[RequirePolicy]`.
- [Policies](core-concepts/policies.md) — `IAuthorizationPolicy`, async-only contract, structured deny.
- [Security context](core-concepts/security-context.md) — `ISecurityContext`, host-specific subinterfaces, the anonymous singleton.
- [Attributes](attributes.md) — `[Policy]`, `[RequirePolicy]`, and `[RequireAnyPolicy]` reference.
- [Host integration](guides/host-integration.md) — how the generator + DI extension fit together.

### v2.1 core-concepts

- [OR composition](core-concepts/or-composition.md) — `[RequireAnyPolicy]`, OR-group semantics, combined-failure shape (`any.all_failed`).
- [Parameterized policies](core-concepts/parameterized-policies.md) — `IAuthorizationPolicy<T1...>` family + compile-time-constant args (arity cap 3).
- [Resource-based authorization](core-concepts/resource-based-authorization.md) — `IResourceSecurityContext<TResource>`; shipped dormant in v2.1 pending host adoption.

### Diagnostics

- [`ZAUTH006`](diagnostics/ZAUTH006.md) — `[RequireAnyPolicy]` with a single policy name (Warning).
- [`ZAUTH007`](diagnostics/ZAUTH007.md) — `[RequirePolicy]` argument shape doesn't match policy interface (Error).
- [`ZAUTH008`](diagnostics/ZAUTH008.md) — `[Policy]` class implements multiple `IAuthorizationPolicy` variants (Error).

---

## Targets

`net8.0`, `net9.0`, `net10.0`. AOT-compatible — `<IsAotCompatible>true</IsAotCompatible>` is set on the main library and the `samples/ZeroAlloc.Authorization.AotSmoke/` app is exercised on every CI run with `PublishAot=true`.
