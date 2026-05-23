---
id: attributes
title: Attributes
sidebar_position: 3
---

# Attributes

Three attributes ship in the contract — `[Policy]`, `[RequirePolicy]`, and
v2.1's `[RequireAnyPolicy]`. None of them do anything on their own. All three
are read at compile time by the bundled source generator, which emits the
dispatcher + DI registrations.

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
    // Parameterless — selects IAuthorizationPolicy.
    public RequirePolicyAttribute(string policyName);

    // v2.1 parameterized overload — selects IAuthorizationPolicy<T1...>.
    public RequirePolicyAttribute(string policyName, params object?[] args);

    public string PolicyName { get; }
    public object?[]? PolicyArgs { get; }
}
```

| Property | Type | Description |
|---|---|---|
| `PolicyName` | `string` | Required. Non-null, non-empty. Must match a `[Policy("...")]` `Name` discoverable from the consumer's compilation or referenced assemblies. The generator emits `ZAUTH001` if the name has no match. |
| `PolicyArgs` | `object?[]?` | v2.1. Optional compile-time-constant arguments forwarded to a parameterized `IAuthorizationPolicy<T1...>` policy (arity 1–3). The generator validates arg shape at compile time and emits `ZAUTH007` on arity or type mismatch. Constants only — `DateTime.Today` or `new Foo()` are rejected by the C# compiler. See [parameterized policies](core-concepts/parameterized-policies.md). |

Targets **classes and structs only**. Methods, interfaces, delegates, and primitives are rejected — the generator emits `ZAUTH005` if `[RequirePolicy]` is placed on any non-class/non-struct target. `AllowMultiple = true` — stacking `[RequirePolicy]` declarations is supported, and the generator emits a dispatcher that requires every named policy to allow (conjunction). `Inherited = false` — derived request types do not inherit the requirement; redeclare on each request type that needs it.

```csharp
[RequirePolicy("AdminOnly")]
public sealed record DeleteUserCommand(string UserId);

[RequirePolicy("ActiveTenant")]
[RequirePolicy("AdminOnly")]
public sealed record PurgeTenantCommand(string TenantId);

// v2.1 — parameterized: forwards the typed 18 to IAuthorizationPolicy<int>.EvaluateAsync.
[RequirePolicy("MinAge", 18)]
public sealed record CreatePostCommand(string Body);
```

## [RequireAnyPolicy] (v2.1)

Binds a request type to an **OR group** — at least one of the named policies
must allow for the group to allow. Stacking `[RequireAnyPolicy]` with
`[RequirePolicy]` (or with another `[RequireAnyPolicy]`) is AND across
attributes.

```csharp
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = true,
    Inherited = false)]
public sealed class RequireAnyPolicyAttribute : Attribute
{
    public RequireAnyPolicyAttribute(params string[] policyNames);
    public string[] PolicyNames { get; }
}
```

| Property | Type | Description |
|---|---|---|
| `PolicyNames` | `string[]` | Required. Each name must match a `[Policy("...")]` `Name`. v2.1 restricts the OR group to **parameterless** policies; parameterized OR is a follow-up. |

```csharp
[RequirePolicy("Admin")]
[RequireAnyPolicy("Premium", "Trusted")]
public sealed record ViewBillingQuery();
// Effective: Admin AND (Premium OR Trusted)
```

When every policy inside an OR group fails, the generator synthesises a
single `AuthorizationFailure` with `Code = "any.all_failed"` and a `Reason`
of the form `"[Name1: reason1] OR [Name2: reason2]"` (declaration order).

`[RequireAnyPolicy]` with a single name fires `ZAUTH006` (Warning) — the
group degenerates to `[RequirePolicy]` and the simpler form is preferred.
See [OR composition](core-concepts/or-composition.md).

## Generator diagnostics

Eight compile-time diagnostics flag wiring mistakes before runtime:

| ID | Severity | Fires when |
|---|---|---|
| `ZAUTH001` | Error | `[RequirePolicy("X")]` references a policy name with no matching `[Policy("X")]`. |
| `ZAUTH002` | Error | Two `[Policy("X")]` declarations share the same name. |
| `ZAUTH003` | Error | A `[Policy]`-decorated class does not implement `IAuthorizationPolicy`. |
| `ZAUTH004` | Error | A `[Policy]`-decorated class is abstract or static. |
| `ZAUTH005` | Error | `[RequirePolicy]` is placed on a non-class/non-struct target. |
| [`ZAUTH006`](diagnostics/ZAUTH006.md) | Warning | `[RequireAnyPolicy]` lists a single policy name (use `[RequirePolicy]`). |
| [`ZAUTH007`](diagnostics/ZAUTH007.md) | Error | `[RequirePolicy("Name", ...)]` arg shape (arity or type) doesn't match the `[Policy]` class's interface. |
| [`ZAUTH008`](diagnostics/ZAUTH008.md) | Error | A `[Policy]` class implements multiple `IAuthorizationPolicy` variants. |

## Discovery

The generator runs at compile time, walks `[Policy]` and `[RequirePolicy]` attributes across the consumer's compilation and referenced assemblies, and emits one `AuthorizerFor<TRequest>` subclass per `[RequirePolicy]`-decorated type plus an `AddZeroAllocAuthorization()` extension method on `IServiceCollection`. Hosts no longer hand-write assembly scans or registry dictionaries.

See [Host integration](guides/host-integration.md) for the generator + DI wiring contract a host is expected to consume.
