# ZeroAlloc.Authorization — Backlog

Candidate features that may move into the core contract once concrete consumer needs emerge. Order is rough priority, not commitment. Nothing here ships speculatively — items graduate when at least two hosts independently want the same thing.

## 1. Policy composition with explicit OR

**What:** allow stacked `[RequirePolicy]` declarations to express OR semantics in addition to the current AND default.

```csharp
[RequirePolicy("Admin")]
[RequirePolicy("Premium")]   // implicit AND — both must pass (current behavior)
public sealed record DeleteUserCommand(string Id);

// Hypothetical OR knob:
[RequirePolicy("Admin", Mode = RequirePolicyMode.Any)]
[RequirePolicy("Premium", Mode = RequirePolicyMode.Any)]
public sealed record ViewBillingQuery();
```

**Why:** policies stay small and reusable; consumers stop writing `AdminAndPremiumPolicy` aggregates. Without this, every combination needs a new `[Policy]` class.

**Open questions:**
- Single attribute with `params string[]` + `Mode` enum, or per-attribute `Mode` property that the generator evaluates?
- Default mode when stacking — keep AND or switch to explicit?
- Short-circuit on first failure or evaluate all (for richer failure reporting)?

**Graduation signal:** at least one host needs OR composition that's awkward to express in the policy body.

**Generator coupling notes:** the bundled generator currently emits AND-only dispatchers. Adding `Mode` requires teaching `AuthorizerForEmitter` to emit a different short-circuit pattern and bumping the generator's emit version.

## 2. Parameterized policies

**What:** policy names accept compile-time arguments that the policy class consumes.

```csharp
[RequirePolicy("MinAge", 18)]
public sealed record ApplyForLicenseCommand(...);

[Policy("MinAge")]
public sealed class MinAgePolicy : IAuthorizationPolicy<int>
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, int minAge, CancellationToken ct = default)
        => new(int.TryParse(ctx.Claims["age"], out var a) && a >= minAge
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("min_age.below", $"age below {minAge}"));
}
```

**Why:** removes the explosion of `MinAge18Policy`, `MinAge21Policy`, etc.

**Open questions:**
- Generic `IAuthorizationPolicy<T>` per arg type, or one base interface with `object[]` parameters?
- Constants only or arbitrary expressions? (Attributes only allow constants.)
- How does the generator handle parameterless and parameterized variants of the same policy name?

**Graduation signal:** a consumer has shipped at least three near-duplicate policies that differ only by a constant.

**Generator coupling notes:** the bundled generator currently reads only the positional `policyName` arg. Adding parameter forwarding requires the generator to capture the additional constructor args from `[RequirePolicy(...)]` and pass them into the policy's `EvaluateAsync` call site.

## 3. Resource-based authorization

**What:** a first-class pattern for "the resource being acted on" beyond per-host `I*SecurityContext` subinterfaces.

```csharp
public interface IResourceSecurityContext<TResource> : ISecurityContext
{
    TResource Resource { get; }
}

public sealed class OwnerOnlyPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx is IResourceSecurityContext<Post> rc && rc.Resource.OwnerId == ctx.Id
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("resource.not_owner"));
}
```

**Why:** today every host invents its own subinterface (`IToolCallSecurityContext`, `IRequestSecurityContext<TRequest>`). A shared `IResourceSecurityContext<T>` lets the *same* policy class work across hosts when the resource type matches.

**Open questions:**
- Generic interface in core, or leave to hosts and accept the divergence?
- How does it compose with the existing host-specific subinterfaces (e.g. is `IRequestSecurityContext<TRequest>` *also* an `IResourceSecurityContext<TRequest>`?)?
- Does the host populate Resource by convention (e.g. mediator request = resource)?

**Graduation signal:** at least two hosts want to share a policy that operates on a typed resource.

**Host coupling notes:** **Runtime + DI surface change required** in `ZeroAlloc.Mediator.Authorization`. Host must populate the typed-resource context with the request being dispatched. AI.Sentinel would similarly need to populate the resource (the tool call) — neither host adopts trivially.

## 4. Standard failure shape — ✅ shipped (v1.0.0)

**Status:** shipped. `AuthorizationFailure` (with `Code`, `Reason`, `DefaultDenyCode`) and `UnitResult<AuthorizationFailure>` from `ZeroAlloc.Results` are the contract's return shape. v2 made them the only return shape — `EvaluateAsync` always emits a `UnitResult<AuthorizationFailure>`.

## 5. Source-generated policy registry — ✅ shipped (v2.0.0)

**Status:** shipped on `feat/policy-registry-generator-v2` (PR #19). The Roslyn generator bundled in the main package discovers `[Policy]`-decorated classes and `[RequirePolicy]`-decorated request types at compile time, emits one `AuthorizerFor<TRequest>` subclass per request, and emits an `AddZeroAllocAuthorization()` extension on `IServiceCollection` that registers everything as scoped.

**What landed:**

- Bundled generator in the main package — no separate `*.Generator` install.
- Five compile-time diagnostics: `ZAUTH001` (unknown policy name), `ZAUTH002` (duplicate name), `ZAUTH003` (`[Policy]` class doesn't implement `IAuthorizationPolicy`), `ZAUTH004` (abstract/static `[Policy]` class), `ZAUTH005` (`[RequirePolicy]` on non-class/non-struct target).
- Cross-assembly discovery — `[Policy]` classes in referenced assemblies are picked up alongside in-compilation policies.
- Attribute rename: `[AuthorizationPolicy]` → `[Policy]`, `[Authorize]` → `[RequirePolicy]`. The name collision with `Microsoft.AspNetCore.Authorization.AuthorizeAttribute` is gone.
- `[RequirePolicy]` is now class/struct-level only (`AttributeTargets.Class | AttributeTargets.Struct`); method-level usage fires `ZAUTH005`. Still `AllowMultiple = true` for stacking.
- `IAuthorizationPolicy` collapsed to a single async method: `ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext, CancellationToken)`.

**Host migration:**

`ZeroAlloc.Mediator.Authorization` v5 has shipped against this contract. The host's `src/ZeroAlloc.Mediator.Authorization.Generator/` was deleted (~150 LOC including `LookupEmitter` + `PolicyDiscovery` + `RequestDiscovery` + hooks + tests). The runtime `AuthorizationBehavior` now resolves `AuthorizerFor<TRequest>` from DI via generic dispatch (matching `Mediator.Validation`'s pattern exactly). AI.Sentinel adoption is the next step.

## 6. Certify the "ZeroAlloc" promise — ✅ DONE (2026-05-06, PR #11)

**Status:** shipped. The package now carries the AOT badge backed by enforcement, not just aspiration.

**What landed:**

- `<IsAotCompatible>true</IsAotCompatible>` on the main library csproj — already in place from earlier work.
- `aot-smoke` CI job publishes the sample with `PublishAot=true` and exercises all four hot-path APIs end-to-end on the AOT-compiled binary.
- `benchmarks/` project with BenchmarkDotNet runs covering `EvaluateAsync` (the v2 single-method contract).
- AOT badge in the README — already in place from earlier work.
- **The missing piece (the core of this item):** a CI-enforceable allocation gate. A 70-LOC `AllocationGate` helper brackets calls with `GC.GetAllocatedBytesForCurrentThread()` and asserts a per-call budget. Used in two places:
  - `tests/AllocationBudgetTests.cs` — JIT-side gate, runs every `dotnet test`. Fails CI if any of the 4 hot-path APIs regresses to allocate.
  - `samples/AotSmoke/Program.cs` — AOT-side gate. Catches trim/escape-analysis regressions the JIT-side test misses. Confirmed `EvaluateAsync (allow)` is genuinely 0 B under AOT runtime.
- Three negative-control self-tests guard the gate itself: `Gate_DetectsAllocation_WhenActionAllocates`, `Gate_RejectsValueTask_NotCompletedSynchronously`, `Gate_TolerantOfWarmupOnlyAllocations`.

**Open questions resolved:**
- Benchmark target: leaf `EvaluateAsync` call only — keeps the gate tight; host-style measurement lives in the host packages where it belongs (e.g. `Mediator.Authorization`'s own gate).
- Allocation budget: strict 0 B for the contract's hot path. Confirmed achievable on both JIT and AOT runtimes.

**Pioneer pattern.** This is the first CI-enforceable allocation gate in the ZeroAlloc family. Sibling packages (Mediator, Cache, Resilience, etc.) can adopt by copying ~70 LOC + declaring their own per-API budget tables. `ZeroAlloc.Mediator.Authorization` shipped today with the same gate (Mediator PR #74), confirming the pattern lifts cleanly.

---

## Out of scope (for now)

- **Multi-tenant primitives.** See "Decisions: multi-tenancy" below for the full reasoning.
- **Rate-limit / throttle as a policy.** Different concern; goes in `ZeroAlloc.Resilience`.
- **Auditing hooks.** Hosts can wire their own; revisit if at least two hosts want shared `IAuthorizationAuditSink`.
- **Caching policy results.** Per-host concern; not all policies are pure.

## Decisions: multi-tenancy

**Decision (today):** do not build a `ZeroAlloc.MultiTenancy` package. Tenant identity rides on `ISecurityContext.Claims["tenant_id"]`. Consumers who need full multi-tenant infrastructure use Finbuckle.MultiTenant alongside ZeroAlloc libraries.

**Why not a Finbuckle replacement:** Finbuckle solves a request-edge concern (resolve once per request, dispatch). The "zero-alloc" angle is marginal there — saving allocations on a once-per-request operation isn't a differentiator. Cloning Finbuckle is months of work for a problem the ecosystem has already solved well.

**Where a ZeroAlloc-shaped multi-tenancy package *would* differentiate**, if the graduation signal fires: source-generated, compile-time-validated tenant-keyed services. Specifically:

1. **`[Tenant("alpha")]`-style attributes** with a Roslyn generator that emits `services.AddKeyedScoped<IFoo, Foo>("alpha")` registrations and a name → service-key lookup table.
2. **Typed accessor `ITenantScoped<T>`** that hides keyed resolution behind a clean API — caller writes `ITenantScoped<IFoo>`, accessor pulls tenant id from `ITenantContext` and does `GetKeyedService<T>(tenantId)` internally. Single point to instrument.
3. **Compile-time guards.** Typo a tenant key (`[Tenant("alphaa")]`) → build fails with a `ZA*` diagnostic. Without a generator that's a runtime `KeyNotFoundException` at first request.

What this package would deliberately *not* do (ceded to Finbuckle / the host):

- Tenant resolution strategies (host / route / header / claim) — the resolver is the host's job; the package consumes whatever populates `ITenantContext`.
- Tenant stores (config / EF / external).
- Multi-database / multi-schema data isolation patterns.
- Per-tenant configuration / `IOptions<T>` overrides.

**Faster keyed-service dispatch is *not* the reason to build this.** A compile-time `switch (tenantId)` over a closed tenant set is faster than `GetKeyedService(string)`'s dictionary lookup, but real multi-tenant systems load tenants from config / DB at startup — the closed-set assumption rarely holds. Don't pitch this on perf.

**Graduation signal:**
- A second ZeroAlloc library independently asks for "give me the current tenant id" beyond what `ISecurityContext.Claims` already provides — *and*
- A concrete consumer (Outbox or Saga, most likely) wants per-tenant service instances and is about to hand-write the keyed-DI registrations.

Until both signals fire, this stays in Decisions, not in the active feature backlog.

## Hosts (separate repos)

These are the consumers that will surface graduation signals. Each is its own repo / package, not part of this backlog directly.

| Host | Status | First version against |
|---|---|---|
| [AI.Sentinel](https://github.com/MarcelRoozekrans/AI.Sentinel) | Shipping | v1 contract (v2 adoption pending) |
| ZeroAlloc.Mediator.Authorization | Shipping (v5) | v2 contract |
| ZeroAlloc.Rest.Authorization (?) | Speculative | TBD |
| ZeroAlloc.Saga.Authorization (?) | Speculative | TBD |

## Versioning contract

Companion packages (`ZeroAlloc.Mediator.Authorization`, `AI.Sentinel`, future hosts) consume the public types of this package as a `<PackageReference>`. The cost of releasing a host is real, so the core has to be deliberate about what it changes and when.

**Companions don't move in lockstep with the core.** A `<PackageReference Include="ZeroAlloc.Authorization" Version="1.0.0" />` is a *floor*, not a fence — NuGet picks the highest compatible version available on the consumer side. A patch or minor bump in the core does not require a companion re-release. Companions only re-release when (a) they want to consume a *new* API, (b) they fix their own bug / add their own feature, or (c) the core ships a major version with a breaking change that affects them.

**SemVer rules for this package:**

| Change | Allowed in 1.x patch / minor? |
|---|---|
| Add a new contract type | Yes — additive |
| Add a new property to `ISecurityContext` | **No** — every implementer breaks. Major. |
| Add a new method on `IAuthorizationPolicy` **with a default implementation** | Yes — default-interface-method makes it non-breaking |
| Add a new method on an interface **without** a default implementation | **No** — implementers break. Major. |
| Tighten parameter nullability / non-null contract | **No** — major |
| Loosen parameter nullability | Yes — additive for consumers |
| Add an optional attribute constructor overload | Yes — additive |
| Rename or remove any public type or member | **No** — major |

For any future addition to `IAuthorizationPolicy`, use the default-interface-method (DIM) pattern: add the new member with a default implementation that delegates to the existing `EvaluateAsync`. Existing implementers stay binary-compatible; new implementers opt in by overriding.

**Engineering gates that enforce this:**

- Wire `Microsoft.CodeAnalysis.PublicApiAnalyzers` (the `RS0016`–`RS0017` family) so any unintended public-API change fires a build error. Track shipped surface in `PublicAPI.Shipped.txt`; require an `RS0017` shipped/unshipped diff in every PR.
- CI runs an API-compat check against the previously released package (e.g. `Microsoft.DotNet.ApiCompat.Tool`) before tagging a release.
- Major version bumps (`2.0`) are coordinated events — open a tracking issue, give companions a deprecation window, ship core and at least the first-party companions together.

**Companion convention:**

- Pin a *floor* matching the lowest core version you actually need. Don't chase the latest core just because it exists.
- Use `Directory.Packages.props` (central package management) to keep all `ZeroAlloc.*` floors in one place per repo.
- Let Renovate / Dependabot open PRs to bump floors. Merge those PRs only when there's a real reason (security, new API needed) — not on every release.

**No back-channels.** The core exposes only public contract types. Do not add `[InternalsVisibleTo]` for companions, do not stash extension points behind `internal` types, do not rely on undocumented runtime behavior. Anything a companion needs has to be a public, documented part of the contract — and therefore subject to the SemVer table above.
