# ZeroAlloc.Authorization v2.1 — design

**Date:** 2026-05-23
**Scope:** Three backlog items promoted from `docs/backlog.md` for the
v2.1 release. All opt-in/additive — no v2.0 contract break. Minor
version bump (2.0.2 → 2.1.0 under release-please's conventional-commits
config).

## Items

| ID | Title | Surface |
|---|---|---|
| #1 | Policy composition with explicit OR | New attribute `[RequireAnyPolicy(params string[] policyNames)]` — sibling to `[RequirePolicy]`. Stacked attrs (across both types) AND together; names within `[RequireAnyPolicy]` form an OR group. |
| #2 | Parameterized policies | New generic interfaces `IAuthorizationPolicy<T1>`, `<T1, T2>`, `<T1, T2, T3>` + `params object?[]? args` overload on `[RequirePolicy]`. Generator emits typed dispatch with literal constants — no boxing. |
| #3 | Resource-based authorization | New interface `IResourceSecurityContext<TResource> : ISecurityContext`. Contract only — host adoption (Mediator.Authorization, AI.Sentinel) is a follow-up. |

Working notes captured during brainstorming live at
[`2026-05-23-authorization-v2.1-decisions-log.md`](2026-05-23-authorization-v2.1-decisions-log.md).
That file records the considered-and-rejected alternatives for each
decision; this document is the canonical specification.

## Goals

- **Each feature additive.** v2.0 declarations stay byte-identical in
  their generator output. Default values on the new properties
  preserve existing behaviour.
- **Type-safe at compile time.** Parameterized-policy argument shapes
  are validated by a new diagnostic (ZAUTH007); no runtime casts in
  policy bodies.
- **Zero-allocation paths preserved.** The dispatch site for
  `[RequirePolicy("Name", const1, const2)]` emits literal arg values —
  no `object[]` allocation, no boxing. OR composition allocates only
  on the all-failed path (the rare/audit path).
- **No host coupling.** v2.1 ships the core contract; host packages
  consume on their own schedule. `IResourceSecurityContext<TResource>`
  in particular is dormant until Mediator.Authorization populates it.
- **Reuse existing machinery.** No new emitter class — extend
  `AuthorizerForEmitter`, `RequestDiscovery`, `PolicyDiscovery`.

## Decisions

### 1. #1 — OR composition

#### 1.1 Surface

```csharp
namespace ZeroAlloc.Authorization;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = true,
    Inherited = false)]
public sealed class RequireAnyPolicyAttribute : Attribute
{
    public RequireAnyPolicyAttribute(params string[] policyNames) => PolicyNames = policyNames;
    public string[] PolicyNames { get; }
}
```

Stacking semantics (mixed AND/OR):

```csharp
[RequirePolicy("Admin")]                                    // AND group element
[RequireAnyPolicy("Premium", "Trusted")]                    // OR group
public record ViewBillingQuery();
// Effective: Admin AND (Premium OR Trusted)
```

#### 1.2 Generator emit

Per `[RequireAnyPolicy(...)]`, the emitter generates short-circuit-on-success
evaluation. On all-fail, it synthesizes a combined `AuthorizationFailure`:

```csharp
// Stub for [RequireAnyPolicy("Premium", "Trusted")] on a request type:
var r_premium = await _premiumPolicy.EvaluateAsync(ctx, ct);
if (r_premium.IsSuccess) goto __or_pass;
var r_trusted = await _trustedPolicy.EvaluateAsync(ctx, ct);
if (r_trusted.IsSuccess) goto __or_pass;

return UnitResult<AuthorizationFailure>.Failure(
    new AuthorizationFailure(
        "any.all_failed",
        $"[Premium: {r_premium.Error.Reason ?? r_premium.Error.Code}] OR [Trusted: {r_trusted.Error.Reason ?? r_trusted.Error.Code}]"));

__or_pass:
// continue with subsequent AND-group elements
```

For mixed AND/OR shapes, the `[RequirePolicy]` and `[RequireAnyPolicy]`
groups emit sequentially. AND-group element failure short-circuits the
whole dispatch (per current v2 semantics). OR-group all-fail synthesises
the combined failure described above and short-circuits at the OR-group
boundary.

Failure-code constant: `"any.all_failed"`. Documented in the runtime
docs so log analytics can grep for it directly.

#### 1.3 New diagnostic

| ID | Severity | Condition |
|---|---|---|
| `ZAUTH006` | Warning | `[RequireAnyPolicy(name)]` declared with a single policy name. The semantic is identical to `[RequirePolicy(name)]`; the consumer probably meant the simpler form. |

### 2. #2 — Parameterized policies

#### 2.1 Surface

Three new generic policy interfaces:

```csharp
namespace ZeroAlloc.Authorization;

public interface IAuthorizationPolicy<T1>
{
    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, T1 arg1, CancellationToken ct = default);
}

public interface IAuthorizationPolicy<T1, T2>
{
    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, T1 arg1, T2 arg2, CancellationToken ct = default);
}

public interface IAuthorizationPolicy<T1, T2, T3>
{
    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, T1 arg1, T2 arg2, T3 arg3, CancellationToken ct = default);
}
```

Extended `[RequirePolicy]` to carry constant args:

```csharp
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = true,
    Inherited = false)]
public sealed class RequirePolicyAttribute : Attribute
{
    // EXISTING ctor — preserved as-is for byte-identical v2.0 declarations.
    public RequirePolicyAttribute(string policyName) => (PolicyName, PolicyArgs) = (policyName, null);

    // NEW ctor — accepts constant args via params.
    public RequirePolicyAttribute(string policyName, params object?[] args)
        => (PolicyName, PolicyArgs) = (policyName, args);

    public string PolicyName { get; }
    public object?[]? PolicyArgs { get; }
}
```

Consumer:

```csharp
[Policy("MinAge")]
public sealed class MinAgePolicy : IAuthorizationPolicy<int>
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, int minAge, CancellationToken ct = default)
        => new(int.TryParse(ctx.Claims["age"], out var a) && a >= minAge
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("min_age.below", $"age below {minAge}"));
}

[RequirePolicy("MinAge", 18)]
public sealed record ApplyForLicenseCommand(...);
```

#### 2.2 Generator emit

For `[RequirePolicy("MinAge", 18)]` the emitter resolves the constant
arg's type at compile time and emits a literal-value dispatch:

```csharp
// Stub for [RequirePolicy("MinAge", 18)]:
var r_minAge = await _minAgePolicy.EvaluateAsync(ctx, 18, ct);
if (r_minAge.IsFailure) return r_minAge;
```

The `18` literal appears inline — no `object[]` allocation, no boxing,
no `Convert.ChangeType`. The generator reads `AttributeData.ConstructorArguments[1]`
(the `params object?[]?` array), finds each `TypedConstant`, and emits
its `Value` as a literal expression with the right C# type.

#### 2.3 New diagnostics

| ID | Severity | Condition |
|---|---|---|
| `ZAUTH007` | Error | `[RequirePolicy("Name", arg1, arg2, ...)]` arg shape doesn't match the `[Policy("Name")]` class's `IAuthorizationPolicy<...>` interface. Arity mismatch (more/fewer args) OR type mismatch (constant type differs from generic parameter). Message includes the offending position, expected type, and supplied type. |
| `ZAUTH008` | Error | `[Policy("Name")]` class implements multiple `IAuthorizationPolicy` variants (parameterless + parameterized, or two different arities). Pick one, or split into separately-named policies. |

### 3. #3 — Resource-based authorization

#### 3.1 Surface

One new interface in the core package:

```csharp
namespace ZeroAlloc.Authorization;

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

#### 3.2 Host coupling

**Out of scope for v2.1.** The core contract ships dormant — hosts
populate `IResourceSecurityContext<TResource>` in their dispatch
behaviour as a follow-up:

| Host | Adoption work |
|---|---|
| `ZeroAlloc.Mediator.Authorization` | Behavior must wrap the incoming `IRequestSecurityContext<TRequest>` (or equivalent) into a type that ALSO implements `IResourceSecurityContext<TRequest>`, exposing the dispatched request as the resource. |
| `AI.Sentinel` | Similar — the tool-call context becomes the resource. |

Until those follow-ups land, `ctx is IResourceSecurityContext<TPost>`
falls through to `false`, and `OwnerOnlyPolicy` returns its failure
case. Consumer code referencing the interface compiles cleanly; runtime
behaviour is "no resource available." Documented in
`docs/core-concepts/resource-based-authorization.md` (created by this
PR's docs task).

#### 3.3 No new diagnostic

`IResourceSecurityContext<TResource>` is just an interface — there's
nothing the generator validates at the contract level. The host's
adoption work may add its own diagnostics (e.g., "policy expects a
resource of type X but host populates type Y") in a future PR.

## Cross-feature compatibility

All pairwise combinations of the v2.1 features are valid:

| Combination | Effect |
|---|---|
| `[RequireAnyPolicy]` + parameterized policy | Names inside `[RequireAnyPolicy]` can be parameterless OR parameterized; generator validates each name's argument shape independently. The OR group is over the boolean results. |
| Parameterized policy + resource-based | A `[Policy("X")] class : IAuthorizationPolicy<T>` can also type-check `ctx is IResourceSecurityContext<U>`. Generic args and resource type are orthogonal axes. |
| `[RequireAnyPolicy]` + resource-based | Identical to single-policy resource handling — each candidate policy in the OR group is free to type-check the resource. |

## New diagnostics tally

| ID | Severity | Condition |
|---|---|---|
| `ZAUTH006` | Warning | `[RequireAnyPolicy]` with a single policy name (use `[RequirePolicy]`). |
| `ZAUTH007` | Error | `[RequirePolicy]` argument shape (arity OR type) doesn't match the `[Policy]` class's interface. |
| `ZAUTH008` | Error | `[Policy]` class implements multiple `IAuthorizationPolicy` variants. |

## Public API additions

`src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt` gains:

```
ZeroAlloc.Authorization.IAuthorizationPolicy<T1>
ZeroAlloc.Authorization.IAuthorizationPolicy<T1>.EvaluateAsync(ZeroAlloc.Authorization.ISecurityContext! ctx, T1 arg1, System.Threading.CancellationToken ct = default) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.UnitResult<ZeroAlloc.Authorization.AuthorizationFailure>>
ZeroAlloc.Authorization.IAuthorizationPolicy<T1, T2>
ZeroAlloc.Authorization.IAuthorizationPolicy<T1, T2>.EvaluateAsync(ZeroAlloc.Authorization.ISecurityContext! ctx, T1 arg1, T2 arg2, System.Threading.CancellationToken ct = default) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.UnitResult<ZeroAlloc.Authorization.AuthorizationFailure>>
ZeroAlloc.Authorization.IAuthorizationPolicy<T1, T2, T3>
ZeroAlloc.Authorization.IAuthorizationPolicy<T1, T2, T3>.EvaluateAsync(ZeroAlloc.Authorization.ISecurityContext! ctx, T1 arg1, T2 arg2, T3 arg3, System.Threading.CancellationToken ct = default) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.UnitResult<ZeroAlloc.Authorization.AuthorizationFailure>>
ZeroAlloc.Authorization.IResourceSecurityContext<TResource>
ZeroAlloc.Authorization.IResourceSecurityContext<TResource>.Resource.get -> TResource
ZeroAlloc.Authorization.RequireAnyPolicyAttribute
ZeroAlloc.Authorization.RequireAnyPolicyAttribute.RequireAnyPolicyAttribute(params string![]! policyNames) -> void
ZeroAlloc.Authorization.RequireAnyPolicyAttribute.PolicyNames.get -> string![]!
ZeroAlloc.Authorization.RequirePolicyAttribute.RequirePolicyAttribute(string! policyName, params object?[]! args) -> void
ZeroAlloc.Authorization.RequirePolicyAttribute.PolicyArgs.get -> object?[]?
```

No changes to `PublicAPI.Shipped.txt`. Strictly additive — no SemVer
break. Minor bump.

## Files touched

Runtime (`src/ZeroAlloc.Authorization/`):

- MOD: `RequirePolicyAttribute.cs` — add `params object?[]` ctor + `PolicyArgs` property.
- NEW: `RequireAnyPolicyAttribute.cs`.
- NEW: `IAuthorizationPolicy.Generic.cs` (or 3 separate files — your call) — the 3 generic interfaces.
- NEW: `IResourceSecurityContext.cs`.
- MOD: `PublicAPI.Unshipped.txt`.

Generator (`src/ZeroAlloc.Authorization.Generator/`):

- MOD: existing `Diagnostics.cs` (or equivalent) — add ZAUTH006, ZAUTH007, ZAUTH008.
- MOD: `RequestDiscovery.cs` (or whichever file walks `[RequirePolicy]` attributes) — extend to read `params object?[]` args + walk `[RequireAnyPolicy]`.
- MOD: `PolicyDiscovery.cs` (or equivalent) — extend to recognise `IAuthorizationPolicy<T>` family + verify single-variant constraint (ZAUTH008).
- MOD: `AuthorizerForEmitter.cs` (or equivalent) — emit typed-arg dispatch + OR-group eval + combined-failure synthesis.
- NEW: ArgValidation helper (or fold into RequestDiscovery) — compile-time validation of arg shape against policy interface arity/types.

Tests (`tests/ZeroAlloc.Authorization.Generator.Tests/`):

- NEW: `RequireAnyPolicyGeneratorTests.cs` — snapshots for OR-group emit, combined-failure shape.
- NEW: `ParameterizedPolicyGeneratorTests.cs` — snapshots for typed-arg dispatch (single arg, two args, three args).
- NEW: `ResourceSecurityContextGeneratorTests.cs` — minimal test verifying the interface compiles + can be implemented by user code.
- MOD: `DiagnosticTests.cs` — positive + negative for ZAUTH006, ZAUTH007 (arity + type variants), ZAUTH008.

Tests (`tests/ZeroAlloc.Authorization.Tests/`):

- NEW: `OrCompositionTests.cs` — runtime OR semantics (any success → success; all fail → combined `AuthorizationFailure`).
- NEW: `ParameterizedPolicyTests.cs` — runtime typed-arg dispatch.
- NEW: `ResourceSecurityContextTests.cs` — runtime test with a user-implemented `IResourceSecurityContext<T>` (e.g., the `Post` example from the design).

Docs (`docs/`):

- MOD: `attributes.md` (or its equivalent — discover the convention) — add `[RequireAnyPolicy]` and updated `[RequirePolicy(args)]` entries.
- NEW: `core-concepts/or-composition.md`.
- NEW: `core-concepts/parameterized-policies.md`.
- NEW: `core-concepts/resource-based-authorization.md` — explicitly documents the dormant-contract semantic + host adoption follow-up.
- NEW (or modify monolithic): `diagnostics/ZAUTH006.md`, `ZAUTH007.md`, `ZAUTH008.md`.
- MOD: `index.md` (or `README.md`) — register the new pages.

## Out of scope

- **Host adoption of `IResourceSecurityContext<TResource>`.** Tracked
  as a follow-up for `ZeroAlloc.Mediator.Authorization` and `AI.Sentinel`.
- **`[Policy]` classes implementing multiple `IAuthorizationPolicy<...>`
  arities.** Diagnosed via ZAUTH008. Could be supported in a future
  version if a real consumer surfaces the case.
- **Code fixes for ZAUTH006 / ZAUTH007.** Could land in a follow-up.
- **`params object?[]?` arg validation beyond arity + type.** Range
  constraints, regex matches on string args, etc. Belong to the policy
  body, not the contract.

## Backward compatibility

Strictly additive:
- New sibling attribute (`[RequireAnyPolicy]`) — existing
  `[RequirePolicy]` declarations stay byte-identical in the generated
  source.
- Three new generic interfaces — existing `IAuthorizationPolicy`
  declarations are unaffected.
- New `params object?[]` overload on `[RequirePolicy]` — the
  single-name ctor is preserved (no obsoletion needed).
- New `IResourceSecurityContext<TResource>` — no v2 implementer
  declares it today; it surfaces only when hosts opt in.
- `PublicAPI.Shipped.txt` untouched.

No SemVer break. Lands as a `feat:` commit; minor bump 2.0.2 → 2.1.0
under release-please's conventional-commits config.
