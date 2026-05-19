---
id: attributes
title: Attributes
sidebar_position: 3
---

# Attributes

Two attributes ship in the contract. Neither does anything on its own — both are read at compile time by the bundled source generator, which emits the dispatcher + DI registrations.

## [Policy]

Names a policy class so it can be referenced from `[RequirePolicy]` and from the generator-emitted lookup.

```csharp
[AttributeUsage(
    AttributeTargets.Class,
    AllowMultiple = false,
    Inherited = false)]
public sealed class PolicyAttribute : Attribute
{
    public PolicyAttribute(string name);
    public string Name { get; }
}
```

| Property | Type | Description |
|---|---|---|
| `Name` | `string` | Required. Non-null, non-empty. The registration name the generator uses to map `[RequirePolicy("Name")]` to this class. |

Targets classes only. `AllowMultiple = false` — one registration name per class. `Inherited = false` — derived policy classes do not inherit the registration; a derived class that is its own policy must declare its own `[Policy("...")]`.

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

The class must implement `IAuthorizationPolicy`. If it does not, the generator emits `ZAUTH003` at compile time. Abstract or static `[Policy]` classes emit `ZAUTH004`. Two `[Policy("X")]` declarations with the same name emit `ZAUTH002`.

## [RequirePolicy]

Binds a request type to one or more named policies.

```csharp
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = true,
    Inherited = false)]
public sealed class RequirePolicyAttribute : Attribute
{
    public RequirePolicyAttribute(string policyName);
    public string PolicyName { get; }
}
```

| Property | Type | Description |
|---|---|---|
| `PolicyName` | `string` | Required. Non-null, non-empty. Must match a `[Policy("...")]` `Name` discoverable from the consumer's compilation or referenced assemblies. The generator emits `ZAUTH001` if the name has no match. |

Targets **classes and structs only**. Methods, interfaces, delegates, and primitives are rejected — the generator emits `ZAUTH005` if `[RequirePolicy]` is placed on any non-class/non-struct target. `AllowMultiple = true` — stacking `[RequirePolicy]` declarations is supported, and the generator emits a dispatcher that requires every named policy to allow (conjunction). `Inherited = false` — derived request types do not inherit the requirement; redeclare on each request type that needs it.

```csharp
[RequirePolicy("AdminOnly")]
public sealed record DeleteUserCommand(string UserId);

[RequirePolicy("ActiveTenant")]
[RequirePolicy("AdminOnly")]
public sealed record PurgeTenantCommand(string TenantId);
```

## Generator diagnostics

Five compile-time diagnostics flag wiring mistakes before runtime:

| ID | Fires when |
|---|---|
| `ZAUTH001` | `[RequirePolicy("X")]` references a policy name with no matching `[Policy("X")]`. |
| `ZAUTH002` | Two `[Policy("X")]` declarations share the same name. |
| `ZAUTH003` | A `[Policy]`-decorated class does not implement `IAuthorizationPolicy`. |
| `ZAUTH004` | A `[Policy]`-decorated class is abstract or static. |
| `ZAUTH005` | `[RequirePolicy]` is placed on a non-class/non-struct target. |

## Discovery

The generator runs at compile time, walks `[Policy]` and `[RequirePolicy]` attributes across the consumer's compilation and referenced assemblies, and emits one `AuthorizerFor<TRequest>` subclass per `[RequirePolicy]`-decorated type plus an `AddZeroAllocAuthorization()` extension method on `IServiceCollection`. Hosts no longer hand-write assembly scans or registry dictionaries.

See [Host integration](guides/host-integration.md) for the generator + DI wiring contract a host is expected to consume.
