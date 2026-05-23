---
id: zauth007
title: ZAUTH007
sidebar_position: 7
---

# ZAUTH007 — [RequirePolicy] argument shape doesn't match policy interface

## Severity

Error.

## Trigger

The `params object?[]` argument list passed to `[RequirePolicy("Name", args...)]`
does not match the target `[Policy("Name")]` class's declared
`IAuthorizationPolicy<...>` interface. The mismatch is one of:

- **Arity** — too few or too many constants for the declared generic arity.
- **Type** — a constant's compile-time type differs from the corresponding
  generic parameter.

The diagnostic message names the offending position, the expected type, and
the supplied type.

## Triggering code

```csharp
[Policy("MinAge")]
public sealed class MinAgePolicy : IAuthorizationPolicy<int>
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, int minAge, CancellationToken ct = default)
        => /* ... */;
}

[RequirePolicy("MinAge")]            // ZAUTH007 — expected 1 argument(s), got 0
public sealed record A();

[RequirePolicy("MinAge", "18")]      // ZAUTH007 — arg 0: expected int, got string
public sealed record B();

[RequirePolicy("MinAge", 18, "x")]   // ZAUTH007 — expected 1 argument(s), got 2
public sealed record C();
```

## Fix

Match the policy's declared interface:

```csharp
[RequirePolicy("MinAge", 18)]        // arity 1, int — matches IAuthorizationPolicy<int>
public sealed record A();
```

Or change the policy's interface declaration if the request site is correct:

```csharp
[Policy("MinAge")]
public sealed class MinAgePolicy : IAuthorizationPolicy<string>  // changed
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, string minAge, CancellationToken ct = default)
        => /* ... */;
}
```

Note that `[RequirePolicy]` arguments must be **C# attribute constants** —
literals, enum constants, `typeof(...)`, or `null`. `DateTime.Today` or
`new Foo()` are rejected by the C# compiler before the generator sees them.

See [parameterized policies](../core-concepts/parameterized-policies.md)
for the full feature reference and the arity-3 cap.
