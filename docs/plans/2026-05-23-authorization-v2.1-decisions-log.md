# Authorization v2.1 — decisions log

Working notes captured during brainstorming. Each entry records what was
locked AND what was considered-and-rejected, with reasoning. When the
brainstorm completes this rolls up into the canonical design doc.

Date opened: 2026-05-23
Bundle: v2.1 graduates #1 (OR composition), #2 (parameterized policies),
#3 (resource-based authorization) from `docs/backlog.md`.

---

## Policy override

The three open backlog items are all marked "graduates when at least two
hosts independently want the same thing." Shipping any of them now is
technically against that documented stance. The user has explicitly
overridden the policy for this bundle, mirroring the precedent set for
Mapping v1.4 today.

---

## D-1: scope — all three items in the bundle

**Locked:** Authorization v2.1 ships #1 + #2 + #3 as additive surface.
Host packages (`ZeroAlloc.Mediator.Authorization`, `AI.Sentinel`) are
NOT bumped in this PR — they consume the new types on their own
schedule. #3 (resource-based) is the most blast-radius item; the core
contract lands here but no host populates the resource yet.

**Rationale:** Same pattern as Mapping v1.4 — speculative graduation but
all additive, no v2 contract break.

**Considered:**
- **Just #1 + #2 (drop #3):** rejected. User wants the full bundle.
- **Coordinate Mediator.Authorization bump alongside #3:** deferred to
  a follow-up; the cross-repo dance benefits from its own dedicated
  brainstorm. v2.1 ships the contract; host adoption follows.

---

## D-2: #1 — OR composition surface

**Locked:** New attribute `[RequireAnyPolicy(params string[] policyNames)]`,
sibling to the existing `[RequirePolicy(string policyName)]`. Stacked
attributes (across both types) AND together; policy names listed
WITHIN a single `[RequireAnyPolicy]` form an OR group.

```csharp
[RequirePolicy("Admin")]
[RequireAnyPolicy("Premium", "Trusted")]
public record ViewBillingQuery();
// Admin AND (Premium OR Trusted)
```

`AttributeUsage`: `Class | Struct, AllowMultiple = true, Inherited = false`
(matches `[RequirePolicy]`).

**Rationale:**
- Attribute name encodes the semantic — a reader instantly knows
  `[RequireAnyPolicy]` means OR; no need to read a `Mode` enum docstring.
- Mixed AND/OR cases compose naturally via independent stacking.
- No PublicAPI break on `[RequirePolicy]`; no `[Obsolete]` shim needed.
- Matches the user's documented memory: "Prefer additive [Obsolete] over
  breaking renames" — this is even better: no Obsolete needed at all.

**Considered:**
- **`Mode = RequirePolicyMode.Any` property on existing `[RequirePolicy]`**
  (the backlog's original proposal): rejected. Mixed-mode grouping rule
  (`(AND of All) AND (OR of Any)`) is non-obvious — a reader has to
  consult docs to interpret a declaration.
- **`params string[]` constructor on `[RequirePolicy]` + `Mode`
  property:** rejected. Requires `PolicyName` → `PolicyNames` rename
  with an `[Obsolete]` shim; net more PublicAPI noise than a new
  sibling attribute.

---

## D-3: #1 — `[RequireAnyPolicy]` failure shape

**Locked:** When all policies in a `[RequireAnyPolicy(...)]` group fail,
the generator synthesizes a combined `AuthorizationFailure`:
- `Code = "any.all_failed"` (constant, greppable in logs/dashboards).
- `Reason = "[NameA: <reasonA or codeA>] OR [NameB: <reasonB or codeB>] OR ..."`
  — concatenated per-policy diagnostics in declaration order.

Emit shape:
```csharp
var r_a = await _aPolicy.EvaluateAsync(ctx, ct);
if (r_a.IsSuccess) return r_a;
var r_b = await _bPolicy.EvaluateAsync(ctx, ct);
if (r_b.IsSuccess) return r_b;
var r_c = await _cPolicy.EvaluateAsync(ctx, ct);
if (r_c.IsSuccess) return r_c;

return UnitResult<AuthorizationFailure>.Failure(
    new AuthorizationFailure(
        "any.all_failed",
        $"[a: {r_a.Error.Reason ?? r_a.Error.Code}] OR [b: {r_b.Error.Reason ?? r_b.Error.Code}] OR [c: {r_c.Error.Reason ?? r_c.Error.Code}]"));
```

**Rationale:** Diagnosability on the failure path beats the
zero-alloc-on-deny micro-optimisation. A user looking at a denied
request log wants to know which of the candidate policies tried, and
why each rejected. Single-policy failure (the original `[RequirePolicy]`
chain) keeps its zero-alloc shape; OR aggregation only happens for
opt-in `[RequireAnyPolicy]` groups, and only when all candidates fail
(the success path is still short-circuit-on-first-success, zero
aggregation).

Cost on the failure path: 1 interpolated string + 1 `AuthorizationFailure`
struct (heap-allocated by the host's logging layer anyway). Failure is
the rare/audit path; this is the right trade.

**Code stability:** `"any.all_failed"` is the canonical code for this
shape. Documented in the runtime docs so log analytics can match on
it directly.

**Considered:**
- **Last-evaluated failure (short-circuit on first success):**
  rejected. Loses N-1 reasons; users debugging "why was this denied?"
  see only one of N candidate failures.
- **First-evaluated failure:** rejected for the same reason — loses
  reasons from later policies.

**Edge case — single-name group.** `[RequireAnyPolicy("Admin")]` (one
name) is semantically identical to `[RequirePolicy("Admin")]`. Fire
a new diagnostic **`ZAUTH006`** (Warning): "[RequireAnyPolicy] with a
single policy name — use [RequirePolicy] for clarity." Auto-fixable
via Roslyn code fix (deferred to a separate task if it gets built).

---

## D-4: #2 — generic `IAuthorizationPolicy<T>` family

**Locked:** Parameterized policies use a family of strongly-typed generic
interfaces:

```csharp
public interface IAuthorizationPolicy<T1> {
    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, T1 arg1, CancellationToken ct = default);
}

public interface IAuthorizationPolicy<T1, T2> { /* ... */ }
public interface IAuthorizationPolicy<T1, T2, T3> { /* ... */ }
```

Three arities ship: 1, 2, 3 generic args. Beyond 3 args, users encode
into a string or use claims.

**Rationale:**
- Type safety at compile time: the policy author's `EvaluateAsync` has
  typed parameters, no casts inside the body.
- Zero-allocation preserved: the generator reads constants from
  `[RequirePolicy("Name", 18, "x", true)]` and emits literal arg values
  in the dispatch site. No boxing, no `object[]` allocation.
- Arity cap at 3 covers the natural cases (single-arg dominates;
  two-arg appears in resource-permission style; three-arg in
  `(action, resource-id, timestamp)` style). Higher arity is rare
  enough that the cap is real.

**Considered:**
- **`IAuthorizationPolicy` extended with `object[]?` arg parameter**
  (additive overload): rejected. Boxes value types, defeats zero-alloc
  promise on the dispatch path, moves type errors from compile-time
  to runtime.
- **Ship only `<T>`:** rejected. Forces users to string-encode
  multi-arg policies — ugly workaround at the declaration site.
- **Ship `<T>` + `<T1, T2>` (stop at 2):** rejected. Three-arg policies
  surface naturally enough that stopping one cliff early is too soon.

---

## D-5: #2 — generator validates argument shape

**Locked:** New diagnostic `ZAUTH007` fires when `[RequirePolicy("Name", arg1, arg2, ...)]`
is incompatible with the `[Policy("Name")]` class's `IAuthorizationPolicy<...>`
interface:
- Arity mismatch (more or fewer args than the policy interface expects).
- Type mismatch (positional arg N has a constant of type U, but the
  policy interface declares Tn for that position).

The diagnostic carries the policy name, the position of the mismatch,
the expected type, and the supplied type. Auto-emission from the
existing `RequestDiscovery` walk in the generator.

**Edge case:** `[Policy("Name")]` class implementing the parameterless
`IAuthorizationPolicy` AND a parameterized `IAuthorizationPolicy<...>`
is NOT supported in v2.1. Diagnostic `ZAUTH008` fires: "[Policy] class
implements multiple IAuthorizationPolicy variants — pick one or split
into separately-named policies." Deferred to a follow-up if a real
consumer asks (rare in practice).

---

## D-6: #3 — resource-based authorization contract

**Locked:** Single new interface in the core package:

```csharp
public interface IResourceSecurityContext<TResource> : ISecurityContext
{
    TResource Resource { get; }
}
```

Policies that need the resource type-check inside their `EvaluateAsync`:

```csharp
public sealed class OwnerOnlyPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx is IResourceSecurityContext<Post> rc && rc.Resource.OwnerId == ctx.Id
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("resource.not_owner"));
}
```

**Host coupling is out of scope.** This PR ships the contract only. Host
packages (`ZeroAlloc.Mediator.Authorization`, `AI.Sentinel`) will
adopt by populating the typed-resource context in their dispatch
behaviour — tracked as a follow-up. Until then, the interface exists
but no host produces an `IResourceSecurityContext<TResource>` at
dispatch time; consumer policies that type-check for it via `is`
simply fall through to the `false` branch.

**Rationale:** Cross-host shared contract — once Mediator.Authorization
and AI.Sentinel adopt, the same `OwnerOnlyPolicy` works for both
hosts when the resource type matches. The interface in core unblocks
that coordination without requiring it to ship simultaneously.

**Considered:**
- **Leave to hosts and accept divergence** (each host invents its own
  `IFooSecurityContext<TFoo>`): rejected. The divergence is the
  problem this item exists to solve — every host already does this
  and the policy classes can't be shared.
- **Ship core + bump hosts simultaneously:** deferred per D-1. Lands
  in a follow-up.

---

(Append additional decisions below as they lock.)
