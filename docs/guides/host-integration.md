---
id: host-integration
title: Host Integration
sidebar_position: 2
---

# Host Integration

ZeroAlloc.Authorization v2 ships the contract **and** a Roslyn source generator. The generator owns the assembly-walk and the name → policy lookup that hosts used to write by hand. A host now does three things: call `AddZeroAllocAuthorization()` at startup, build an `ISecurityContext` per request, and resolve `AuthorizerFor<TRequest>` from DI at dispatch.

See also: [policies](../core-concepts/policies.md), [security-context](../core-concepts/security-context.md), [attributes](../attributes.md).

---

## What the generator emits

When a consumer's compilation includes `[Policy]`-decorated classes and `[RequirePolicy]`-decorated request types, the generator emits two artifacts under the `ZeroAlloc.Authorization.Generated` namespace:

1. **One `AuthorizerFor<TRequest>` subclass per `[RequirePolicy]`-decorated type.** Each subclass resolves the named `[Policy]` classes from DI, calls their `EvaluateAsync` in declaration order, and returns the first failure. All policies must allow before the dispatcher returns `Success()` (conjunction).
2. **`AddZeroAllocAuthorization()` extension on `IServiceCollection`.** Registers every `[Policy]` class as scoped and every emitted `AuthorizerFor<T>` as scoped, plus the `ZeroAllocAuthorizationMarker` singleton. The method is emitted even when the compilation has no `[Policy]` or `[RequirePolicy]`; it then registers only the marker, so a composition root can call it before the first policy exists. Hosts and integrations probe the service collection for `ZeroAllocAuthorizationMarker` to know the method ran. Calling it more than once is safe.

    In a layered solution the generator runs in every project that references the package. A project with no `[Policy]` and no `[RequirePolicy]` emits the registration as `internal`, so it never leaks to the projects above it, and emits nothing at all when a referenced assembly already exposes `GeneratedAuthorizationRegistration`. A project that declares or sees policies emits it `public`. The host therefore always binds to the registration of the layer that has the policies. A library that ships policies to other apps opts out of the `public` registration, see [Libraries that ship policies](#libraries-that-ship-policies).

Five diagnostics fire at compile time if the consumer's wiring is broken — `ZAUTH001` (unknown policy name), `ZAUTH002` (duplicate policy name), `ZAUTH003` (policy class doesn't implement `IAuthorizationPolicy`), `ZAUTH004` (abstract/static policy class), `ZAUTH005` (`[RequirePolicy]` on a non-class/non-struct target). The host inherits these as a compile-time safety net — wiring mistakes never reach runtime.

---

## Libraries that ship policies

A library that declares `[Policy]` classes, such as a NuGet package, runs the generator too, and by default emits a `public` `GeneratedAuthorizationRegistration`. An app that references the library and declares policies of its own emits a second one. Both declare the same extension method in the same namespace, so the app's call does not compile:

```text
error CS0121: The call is ambiguous between the following methods or properties:
'ZeroAlloc.Authorization.Generated.GeneratedAuthorizationRegistration.AddZeroAllocAuthorization(IServiceCollection)' and
'ZeroAlloc.Authorization.Generated.GeneratedAuthorizationRegistration.AddZeroAllocAuthorization(IServiceCollection)'
```

Renaming or wrapping the class does not help: the extension method signature stays the same. The library sets the `ZeroAllocAuthorizationRegistration` MSBuild property instead:

```xml
<PropertyGroup>
  <ZeroAllocAuthorizationRegistration>internal</ZeroAllocAuthorizationRegistration>
</PropertyGroup>
```

| Value | Registration |
|---|---|
| unset or empty | `public` when the project declares or sees policies or `[RequirePolicy]` types, `internal` when it has none. The default. |
| `public` | The same as unset, stated explicitly. |
| `internal` | Always `internal`. |

The value is case-insensitive. Any other value raises [`ZAUTH009`](../diagnostics/ZAUTH009.md) and the default applies.

With the library's registration internal:

- the library can still call `AddZeroAllocAuthorization()` itself, for instance from its own `AddMyLibrary()` extension;
- the app's call binds to the app's own registration, which registers the app's policies **and** the library's, because the generator sees the library's `[Policy]` classes in the reference;
- an internal registration never counts as "a referenced assembly already exposes the registration", so a project above the library that has no policies still gets its own internal one.

Set it in every library that ships policies to apps you do not control. An application's own policy layer in a layered solution keeps the default, so its host can call the layer's registration. A project that grants the library `InternalsVisibleTo` can see the internal registration, and a call from such a project is ambiguous again.

The property reaches the generator through `CompilerVisibleProperty`, which both the `ZeroAlloc.Authorization` and the `ZeroAlloc.Authorization.Generator` packages declare. A project that references the generator as a `ProjectReference` instead of a package adds `<CompilerVisibleProperty Include="ZeroAllocAuthorizationRegistration" />` itself.

---

## Host startup

One line:

```csharp
using ZeroAlloc.Authorization.Generated;

builder.Services.AddZeroAllocAuthorization();
```

That registers every `[Policy]` class discovered in the consumer's compilation plus every emitted `AuthorizerFor<T>`. If a specific policy needs a non-default lifetime (singleton for pure-CPU, transient for short-lived state), register it explicitly **after** the call:

```csharp
services.AddZeroAllocAuthorization();
services.AddSingleton<AdminOnlyPolicy>(); // overrides scoped registration
```

---

## Host dispatch

For each incoming request, build an `ISecurityContext` (or a host-specific subinterface — see below), resolve `AuthorizerFor<TRequest>` from the DI scope, and call `EvaluateAsync`:

```csharp
var authorizer = sp.GetService<AuthorizerFor<TRequest>>();
if (authorizer is not null)
{
    var result = await authorizer.EvaluateAsync(securityContext, ct);
    if (result.IsFailure)
    {
        // host translates result.Error.Code / Reason into HTTP 403 or equivalent
        return Forbid(result.Error);
    }
}

// no AuthorizerFor<TRequest> registered → request has no policies → proceed
```

The `GetService<>` (not `GetRequiredService<>`) call is deliberate. A request type with no `[RequirePolicy]` attributes has no emitted dispatcher and no registration; the host treats `null` as "no policies for this request" and proceeds. If your host's policy is that every request type *must* have an authorizer, use `GetRequiredService<>` and let the missing registration throw.

---

## Host-specific subinterfaces

`ISecurityContext` carries three fields. When a host needs to flow more — a tool name, a request payload, an HTTP path — it defines a subinterface and policies downcast to read it.

**AI.Sentinel — tool-call host:**

```csharp
public interface IToolCallSecurityContext : ISecurityContext
{
    string ToolName { get; }
    IReadOnlyDictionary<string, object?> Args { get; }
}
```

The host populates the subinterface when constructing the per-call context, then passes it as `ISecurityContext` to the policy. The policy downcasts inside the body when it cares:

```csharp
[Policy("NoDestructiveTools")]
public sealed class NoDestructiveToolsPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
    {
        if (ctx is IToolCallSecurityContext tc && tc.ToolName == "delete_database")
            return new(new AuthorizationFailure("tool.destructive", "destructive tool blocked"));
        return new(UnitResult<AuthorizationFailure>.Success());
    }
}
```

**ZeroAlloc.Mediator.Authorization v5 — request-scoped host:**

```csharp
public interface IRequestSecurityContext<TRequest> : ISecurityContext
{
    TRequest Request { get; }
}
```

Same pattern — the host wraps the live request in the typed subinterface, the policy downcasts to read it. A policy that *requires* the host context (i.e. it makes no sense outside that host) should fail closed when the downcast misses, rather than silently allow.

---

## Mapping `AuthorizationFailure.Code` to outcomes

The whole point of `EvaluateAsync` returning a coded failure is that hosts switch on `Code` to produce structured responses. Each host translates differently.

**HTTP host — switch to a status:**

```csharp
var result = await authorizer.EvaluateAsync(ctx, ct);
if (result.IsSuccess) return Results.Ok(payload);

return result.Error.Code switch
{
    "tenant.inactive"   => Results.StatusCode(423),  // Locked
    "policy.deny.role"  => Results.Forbid(),          // 403
    "scope.missing"     => Results.StatusCode(403),
    _                   => Results.Unauthorized(),    // 401
};
```

**Mediator host — convert to a result type:**

```csharp
var result = await authorizer.EvaluateAsync(ctx, ct);
if (result.IsSuccess) return await next(request, ct);

// Either bubble as a typed Result<T, AuthorizationFailure>...
return Result.Failure<TResponse, AuthorizationFailure>(result.Error);

// ...or throw a typed exception the pipeline knows about:
throw new UnauthorizedException(result.Error.Code, result.Error.Reason);
```

**Tool-call host — refuse and emit a structured rejection:**

```csharp
var result = await authorizer.EvaluateAsync(ctx, ct);
if (!result.IsSuccess)
{
    return new ToolCallRejection(
        toolName: ctx.ToolName,
        code: result.Error.Code,
        message: result.Error.Reason ?? "Tool call denied.");
}
```

The shape of the response is host-specific; what matters is that the policy emits a *machine-readable code* and the host has the freedom to map it.

For full deny-code conventions and the `AuthorizationFailure` shape, see [failure-shape](../core-concepts/failure-shape.md).

---

## Migrating a v1 host

If you previously wrote your own assembly scanner + `Dictionary<string, IAuthorizationPolicy>` registry to power dispatch, delete it. The generator owns that work now. Concrete migration steps for a host:

1. Delete your `*.Generator` project (or the equivalent reflection-based discovery code).
2. Replace `Dictionary<string, IAuthorizationPolicy>` lookups with `sp.GetService<AuthorizerFor<TRequest>>()`.
3. Drop any `[ModuleInitializer]` wiring you had registering policies at process startup — `AddZeroAllocAuthorization()` handles it through DI generic dispatch.
4. Replace `[Authorize("Name")]` references on method targets with `[RequirePolicy("Name")]` on the containing request type.

`ZeroAlloc.Mediator.Authorization` shipped its v5 against this contract — its old generator was deleted (~150 LOC including hooks + tests) and the runtime `AuthorizationBehavior` migrated to consume `AuthorizerFor<TRequest>` from DI generic dispatch directly.
