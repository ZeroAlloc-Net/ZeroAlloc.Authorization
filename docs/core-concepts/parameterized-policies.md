---
id: parameterized-policies
title: Parameterized Policies
sidebar_position: 7
---

# Parameterized Policies

A policy that varies by a single constant — minimum age, required scope,
plan tier — used to need one class per value:

```csharp
[Policy("MinAge18")] public sealed class MinAge18Policy : IAuthorizationPolicy { ... }
[Policy("MinAge21")] public sealed class MinAge21Policy : IAuthorizationPolicy { ... }
[Policy("MinAge65")] public sealed class MinAge65Policy : IAuthorizationPolicy { ... }
```

Three classes, three registrations, three near-identical bodies. v2.1 collapses
the family into one parameterized policy plus a typed argument at the call site.

---

## The generic interfaces

```csharp
public interface IAuthorizationPolicy<in T1>
{
    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, T1 arg1, CancellationToken ct = default);
}

public interface IAuthorizationPolicy<in T1, in T2> { /* + T2 arg2 */ }
public interface IAuthorizationPolicy<in T1, in T2, in T3> { /* + T3 arg3 */ }
```

Arity is capped at **3**. Beyond three, encode the arguments as a single
string or surface them via `ISecurityContext.Claims` — the generator
intentionally does not chase higher arities.

A single `[Policy]` class implements **one** of these variants. Mixing the
parameterless `IAuthorizationPolicy` with any `IAuthorizationPolicy<...>`
variant, or implementing two different arities on the same class, fires
**ZAUTH008**.

---

## Worked example: MinAgePolicy

```csharp
[Policy("MinAge")]
public sealed class MinAgePolicy : IAuthorizationPolicy<int>
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, int minAge, CancellationToken ct = default)
        => new(ctx is IAgedContext ac && ac.Age >= minAge
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("age.too_young", $"requires age >= {minAge}"));
}

[RequirePolicy("MinAge", 18)] public sealed record CreatePostCommand(string Body);
[RequirePolicy("MinAge", 21)] public sealed record OrderDrinkCommand(string Sku);
```

One class, two bindings, two different threshold values — and the generator
emits **typed** dispatch (the `int 18` flows straight through to
`EvaluateAsync(ctx, 18, ct)` with no boxing).

---

## Compile-time-constant args only

The `params object?[]` overload on `[RequirePolicy]` is a C# attribute
argument list — every entry must be a **constant** the compiler can encode.

```csharp
[RequirePolicy("MinAge", 18)]                 // OK — int literal
[RequirePolicy("Scope", "billing:write")]     // OK — string literal
[RequirePolicy("Tier", PlanTier.Premium)]     // OK — enum constant
[RequirePolicy("MinAge", GetMinAge())]        // ERROR — not a constant
[RequirePolicy("Window", DateTime.Today)]     // ERROR — not a constant
[RequirePolicy("Owner", new OwnerSpec(...))]  // ERROR — not a constant
```

This is a C# language constraint on attribute arguments, not something the
generator enforces. The compiler rejects the non-constant cases before the
generator sees them.

---

## Argument-shape validation — ZAUTH007

The generator validates each `[RequirePolicy("Name", args...)]` against the
target `[Policy("Name")]` class's declared interface:

- **Arity mismatch** — too few or too many args.
- **Type mismatch** — constant type differs from the generic parameter.

Both fire **ZAUTH007** at compile time. The diagnostic message names the
offending position, the expected type, and the supplied type.

```csharp
[Policy("MinAge")] class MinAgePolicy : IAuthorizationPolicy<int> { ... }

[RequirePolicy("MinAge")]            // ZAUTH007 — expected 1 arg, got 0
[RequirePolicy("MinAge", "18")]      // ZAUTH007 — arg 0: expected int, got string
[RequirePolicy("MinAge", 18, "x")]   // ZAUTH007 — expected 1 arg, got 2
```

See [`ZAUTH007`](../diagnostics/ZAUTH007.md) and
[`ZAUTH008`](../diagnostics/ZAUTH008.md) for the full diagnostic surface.

---

## DI registration

The generator registers parameterized policies the same way as parameterless
ones — scoped, by their concrete class type. The args are not part of the DI
identity; the dispatcher forwards the literals from the attribute to
`EvaluateAsync` at the call site. Two requests that bind `[RequirePolicy("MinAge", 18)]`
and `[RequirePolicy("MinAge", 21)]` share a single `MinAgePolicy` instance per
scope.

---

## See also

- [Attributes reference](../attributes.md) — full property tables.
- [OR composition](or-composition.md) — the parameterless OR cousin (v2.1
  parameterized OR is a follow-up).
- [Policies](policies.md) — the parameterless `IAuthorizationPolicy` baseline.
