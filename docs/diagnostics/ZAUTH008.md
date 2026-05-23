---
id: zauth008
title: ZAUTH008
sidebar_position: 8
---

# ZAUTH008 — [Policy] class implements multiple IAuthorizationPolicy variants

## Severity

Error.

## Trigger

A single `[Policy("Name")]` class implements more than one variant of the
`IAuthorizationPolicy` family — for example, both the parameterless
`IAuthorizationPolicy` and the one-arg `IAuthorizationPolicy<int>`, or two
different generic arities at once.

The generator dispatches a policy by its registration name, and that name
must resolve to exactly one interface shape. A class that implements two
variants is ambiguous: a `[RequirePolicy("Name")]` reference cannot decide
which `EvaluateAsync` overload to forward to.

## Triggering code

```csharp
[Policy("MinAge")]
public sealed class MinAgePolicy
    : IAuthorizationPolicy,                // parameterless
      IAuthorizationPolicy<int>            // one-arg — ZAUTH008
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => /* ... */;

    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, int minAge, CancellationToken ct = default)
        => /* ... */;
}
//                   ^^^^^^^^^^^^^^
// ZAUTH008: [Policy("MinAge")] class 'MinAgePolicy' implements multiple
// IAuthorizationPolicy variants (IAuthorizationPolicy, IAuthorizationPolicy<int>);
// pick one or split into separately-named policies.
```

## Fix

Pick one variant:

```csharp
[Policy("MinAge")]
public sealed class MinAgePolicy : IAuthorizationPolicy<int>
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, int minAge, CancellationToken ct = default)
        => /* ... */;
}
```

Or split into separately-named policies — one registration per variant:

```csharp
[Policy("AnyAge")]
public sealed class AnyAgePolicy : IAuthorizationPolicy { /* ... */ }

[Policy("MinAge")]
public sealed class MinAgePolicy : IAuthorizationPolicy<int> { /* ... */ }
```

See [parameterized policies](../core-concepts/parameterized-policies.md)
for the rules around the parameterized policy family.
