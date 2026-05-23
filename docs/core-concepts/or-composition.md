---
id: or-composition
title: OR Composition
sidebar_position: 6
---

# OR Composition

`[RequirePolicy]` is conjunction — every listed policy must allow. v2 had no
explicit disjunction. The escape hatch was a hand-written aggregate:

```csharp
[Policy("AdminOrPremium")]
public sealed class AdminOrPremiumPolicy(AdminOnlyPolicy admin, PremiumPolicy prem)
    : IAuthorizationPolicy
{
    public async ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
    {
        var a = await admin.EvaluateAsync(ctx, ct).ConfigureAwait(false);
        if (a.IsSuccess) return a;
        return await prem.EvaluateAsync(ctx, ct).ConfigureAwait(false);
    }
}
```

That class composes nothing — it duplicates dispatch logic, leaks the policies
it wraps into its constructor, and grows quadratically as composition shapes
multiply (`AdminOrPremium`, `AdminOrPremiumOrTrusted`, …). v2.1 ships
`[RequireAnyPolicy]` so the OR group is declared at the request site and the
generator emits the dispatch.

---

## [RequireAnyPolicy]

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

Names listed inside one `[RequireAnyPolicy(...)]` form an **OR group** — at
least one must allow for the group to allow. Stacking `[RequireAnyPolicy]` with
`[RequirePolicy]` (or with another `[RequireAnyPolicy]`) is AND across
attributes:

```csharp
[RequirePolicy("Admin")]
[RequireAnyPolicy("Premium", "Trusted")]
public sealed record ViewBillingQuery();
// Effective: Admin AND (Premium OR Trusted)
```

The generator emits one OR-group block per `[RequireAnyPolicy]` attribute and
chains them inside the conjunctive `AuthorizerFor<TRequest>` body. Each policy
inside a group is evaluated in declaration order; the **first success
short-circuits** the group.

---

## Combined-failure shape

When every policy inside an OR group fails, the generator synthesises a
single `AuthorizationFailure` for that group:

```csharp
Code   = "any.all_failed"
Reason = "[Name1: reason1] OR [Name2: reason2]"
```

Per-policy entries appear in **declaration order**. If an individual policy
returned a null `Reason`, the synthesiser substitutes its `Code` —
`[Name: code]` — so the combined reason never contains the literal text
`null`.

```csharp
var result = await authorizer.EvaluateAsync(ctx, ct);
if (!result.IsSuccess && result.Error.Code == "any.all_failed")
{
    // result.Error.Reason carries the per-policy breakdown.
    _logger.LogWarning("all OR candidates denied: {Reason}", result.Error.Reason);
}
```

`"any.all_failed"` is a stable contract — hosts can switch on it the same way
they switch on `"policy.deny"` or any policy-author-chosen code.

---

## v2.1 constraint: parameterless policies only

OR-group entries must reference **parameterless** `IAuthorizationPolicy`
classes — `IAuthorizationPolicy<T1>` (and the two- / three-arg variants) are
not yet accepted inside `[RequireAnyPolicy]`. Parameterized OR is a follow-up;
until it lands, place parameterized policies in `[RequirePolicy]` and reserve
`[RequireAnyPolicy]` for the boolean OR over parameterless candidates.

---

## Single-name warning — ZAUTH006

`[RequireAnyPolicy("Solo")]` with exactly one policy name carries no OR
semantics; the generator emits **ZAUTH006** (Warning) to nudge consumers
toward the simpler `[RequirePolicy("Solo")]`. The dispatcher still compiles —
the diagnostic is a clarity nudge, not a hard error.

See [`ZAUTH006`](../diagnostics/ZAUTH006.md) for the trigger and fix.

---

## See also

- [Attributes reference](../attributes.md) — full property tables.
- [Failure shape](failure-shape.md) — `AuthorizationFailure.Code` conventions.
- [Parameterized policies](parameterized-policies.md) — the typed-arg cousin.
