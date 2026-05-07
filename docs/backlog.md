# ZeroAlloc.Authorization — Backlog

Candidate features that may move into the core contract once concrete consumer needs emerge. Order is rough priority, not commitment. Nothing here ships speculatively — items graduate when at least two hosts independently want the same thing.

## 1. Policy composition

**What:** allow multiple policy names to compose on a single method, with explicit AND / OR semantics.

```csharp
[Authorize("Admin")]
[Authorize("Premium")]                 // implicit AND — both must pass
public Task DeleteUserAsync(string id);

[Authorize("Admin", "Premium", Mode = AuthorizeMode.Any)]   // OR — either passes
public Task ViewBillingAsync();
```

**Why:** policies stay small and reusable; consumers stop writing `AdminAndPremiumPolicy` aggregates. Without this, every combination needs a new `[AuthorizationPolicy]` class.

**Open questions:**
- Single attribute with `params string[]` + `Mode` enum, or stackable attributes that the host evaluates?
- Default mode when stacking — AND or OR?
- Short-circuit on first failure or evaluate all (for richer failure reporting)?

**Graduation signal:** at least one host (likely Mediator) needs to express AND/OR before v1.0 of that host.

**Host coupling notes:** **Generator update required** in `ZeroAlloc.Mediator.Authorization`. The host's generator must read `Mode = AuthorizeMode.Any` and emit OR-evaluation instead of the current sequential AND. Without the host update, the generator silently emits AND code for OR-mode attributes — semantic regression. Currently mitigated in the host by `ZAMA005` ("future contract attribute property detected"), which fires for any named arg on `[Authorize]` and tells the user to upgrade. AI.Sentinel handles policy composition entirely in user code today; not affected.

## 2. Parameterized policies

**What:** policy names accept compile-time arguments that the policy class consumes.

```csharp
[Authorize("MinAge", 18)]
public Task ApplyForLicenseAsync(...);

[AuthorizationPolicy("MinAge")]
public sealed class MinAgePolicy : IAuthorizationPolicy<int>
{
    public bool IsAuthorized(ISecurityContext ctx, int minAge) =>
        int.TryParse(ctx.Claims["age"], out var a) && a >= minAge;
}
```

**Why:** removes the explosion of `MinAge18Policy`, `MinAge21Policy`, etc.

**Open questions:**
- Generic `IAuthorizationPolicy<T>` per arg type, or one base interface with `object[]` parameters?
- Constants only or arbitrary expressions? (Attributes only allow constants.)
- How does the registry handle parameterless and parameterized variants of the same policy name?

**Graduation signal:** a host has shipped at least three near-duplicate policies that differ only by a constant.

**Host coupling notes:** **Generator update required** in `ZeroAlloc.Mediator.Authorization`. The host's generator must forward the constructor args from `[Authorize("MinAge", 18)]` to the policy resolver (currently it only reads the policy-name positional arg). Without the host update, args silently ignored. Same `ZAMA005` mitigation as item #1.

## 3. Resource-based authorization

**What:** a first-class pattern for "the resource being acted on" beyond per-host `I*SecurityContext` subinterfaces.

```csharp
public interface IResourceSecurityContext<TResource> : ISecurityContext
{
    TResource Resource { get; }
}

public sealed class OwnerOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) =>
        ctx is IResourceSecurityContext<Post> rc && rc.Resource.OwnerId == ctx.Id;
}
```

**Why:** today every host invents its own subinterface (`IToolCallSecurityContext`, planned `IRequestSecurityContext<TRequest>`). A shared `IResourceSecurityContext<T>` lets the *same* policy class work across hosts when the resource type matches.

**Open questions:**
- Generic interface in core, or leave to hosts and accept the divergence?
- How does it compose with the existing host-specific subinterfaces (e.g. is `IRequestSecurityContext<TRequest>` *also* an `IResourceSecurityContext<TRequest>`?)?
- Does the host populate Resource by convention (e.g. mediator request = resource)?

**Graduation signal:** at least two hosts want to share a policy that operates on a typed resource.

**Host coupling notes:** **Runtime + DI surface change required** in `ZeroAlloc.Mediator.Authorization`. Host must populate the typed-resource context with the request being dispatched — neither `WithAuthorization()` nor the generator currently know about request-as-resource. New runtime API needed (e.g. `opts.UseResourceBoundContext()`); generator must emit per-request resource binding. Likely a v4.x → v4.y minor for the host. AI.Sentinel would similarly need to populate the resource (the tool call) — neither host adopts trivially.

## 4. Standard failure shape

**What:** replace `bool IsAuthorized(...)` with a richer return type that carries deny reason / failure code / message.

```csharp
public readonly struct AuthorizationResult
{
    public bool IsAuthorized { get; }
    public string? FailureReason { get; }
    public string? FailureCode { get; }
}

public interface IAuthorizationPolicy
{
    AuthorizationResult Evaluate(ISecurityContext ctx);
}
```

**Why:** today hosts have to invent their own `UnauthorizedException` / `Forbid()` / `Result.Fail("...")` shapes. A shared structured result lets a host emit consistent telemetry / API responses across policy types.

**Open questions:**
- Use `ZeroAlloc.Results` (`Result<Unit, AuthorizationFailure>`) for ecosystem consistency, or keep the contract dependency-free?
- Breaking change vs additive — keep `IsAuthorized` as a default-implementation thin wrapper over `Evaluate`?
- Async story — `ValueTask<AuthorizationResult> EvaluateAsync(...)`?

**Graduation signal:** two hosts have built non-trivial deny-reason logic on top of the boolean and want to unify it.

**Risk:** highest-impact change — touches every existing policy implementation. Defer until the migration cost is justified.

**Host coupling notes:** **Runtime + DI surface change required** in every host. `ZeroAlloc.Mediator.Authorization` exposes `AuthorizationDeniedException` (carrying `AuthorizationFailure`) and `Result<T, AuthorizationFailure>` directly to user code; if the contract's failure shape changes shape (new fields, signature changes), both surfaces shift. Major version bump of the host. AI.Sentinel exposes a similar deny payload through its tool-call result envelope. Both hosts must coordinate on the same major-version cadence.

## 5. Source-generated policy registry

**What:** a Roslyn generator that discovers all `[AuthorizationPolicy]` types at compile time and emits:
- A name → factory lookup table (no reflection scanning at startup)
- A DI extension `services.AddZeroAllocAuthorization()` that registers all policies
- Compile-time diagnostics for duplicate names, name typos in `[Authorize]`, missing policy classes for referenced names

**Why:** today every host independently scans assemblies for `[AuthorizationPolicy]` types. A shared generator removes the reflection scan, eliminates a class of "I forgot to register the policy" bugs, and gives compile-time errors instead of runtime ones. Aligns with the rest of the ZeroAlloc ecosystem (Mediator, Validation, Inject all source-generate registries).

**Open questions:**
- New package `ZeroAlloc.Authorization.Generator`, or bundled into the main package (per the recent collapse to single-package install)?
- How does it interact with policies defined in different assemblies / packages? (One `[assembly: ZeroAllocAuthorization]` attribute per consumer assembly, like `Inject`?)
- Diagnostic IDs — `ZA1500` series?

**Graduation signal:** the second host (Mediator.Authorization) is being built and the author finds themselves writing scan-and-register code.

**Risk:** medium — generator is a new build artifact, must be Native AOT-safe and run cleanly across SDK versions.

**Host coupling notes:** **This item's graduation signal has fired.** `ZeroAlloc.Mediator.Authorization` shipped (PR ZeroAlloc-Net/ZeroAlloc.Mediator#74) with its own host-side `GeneratedAuthorizationLookup` generator — exactly the scan-and-register code the graduation signal anticipated. When this contract-side generator ships, the host's `src/ZeroAlloc.Mediator.Authorization.Generator/` (~80 LOC of `LookupEmitter` + `PolicyDiscovery` + `RequestDiscovery`) gets deleted; the runtime `AuthorizationBehavior` migrates to consume `AuthorizerFor<TRequest>` via DI generic dispatch (matching `Mediator.Validation`'s pattern exactly). AI.Sentinel will adopt similarly — its current scan-and-register code becomes redundant.

Concrete migration path for the host once #5 ships:
1. Delete `src/ZeroAlloc.Mediator.Authorization.Generator/` entirely.
2. Replace `MediatorAuthorizationGeneratedHooks.GetPoliciesForRequestType<T>()` with `sp.GetService<AuthorizerFor<T>>()`.
3. Replace `Resolve(name, sp)` with the same accessor pattern.
4. Drop the `[ModuleInitializer]` wiring — DI generic dispatch handles everything.
Net code reduction in the host: ~150 LOC (generator + hooks + tests for both).

## 6. Certify the "ZeroAlloc" promise — ✅ DONE (2026-05-06, PR #11)

**Status:** shipped. The package now carries the AOT badge backed by enforcement, not just aspiration.

**What landed:**

- `<IsAotCompatible>true</IsAotCompatible>` on the main library csproj — already in place from earlier work.
- `aot-smoke` CI job publishes the sample with `PublishAot=true` and exercises all four hot-path APIs end-to-end on the AOT-compiled binary.
- `benchmarks/` project with BenchmarkDotNet runs covering `IsAuthorized`, `IsAuthorizedAsync`, `Evaluate`, `EvaluateAsync` — already in place from earlier work.
- AOT badge in the README — already in place from earlier work.
- **The missing piece (the core of this item):** a CI-enforceable allocation gate. A 70-LOC `AllocationGate` helper brackets calls with `GC.GetAllocatedBytesForCurrentThread()` and asserts a per-call budget. Used in two places:
  - `tests/AllocationBudgetTests.cs` — JIT-side gate, runs every `dotnet test`. Fails CI if any of the 4 hot-path APIs regresses to allocate.
  - `samples/AotSmoke/Program.cs` — AOT-side gate. Catches trim/escape-analysis regressions the JIT-side test misses. Confirmed `EvaluateAsync (allow)` is genuinely 0 B under AOT runtime.
- Three negative-control self-tests guard the gate itself: `Gate_DetectsAllocation_WhenActionAllocates`, `Gate_RejectsValueTask_NotCompletedSynchronously`, `Gate_TolerantOfWarmupOnlyAllocations`.

**Open questions resolved:**
- Benchmark target: leaf `IsAuthorized` calls only — keeps the gate tight; host-style measurement lives in the host packages where it belongs (e.g. `Mediator.Authorization`'s own gate).
- Allocation budget: strict 0 B for all four hot-path APIs. Confirmed achievable on both JIT and AOT runtimes.

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
| [AI.Sentinel](https://github.com/MarcelRoozekrans/AI.Sentinel) | Shipping | v1 contract |
| ZeroAlloc.Mediator.Authorization | Planned | v1 contract |
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

The existing async contract (`ValueTask<bool> IsAuthorizedAsync(...)` with a default implementation) is the template: when async support was added, the DIM pattern kept implementers of the original interface working. Use the same pattern for any future contract evolution.

**Engineering gates that enforce this:**

- Wire `Microsoft.CodeAnalysis.PublicApiAnalyzers` (the `RS0016`–`RS0017` family) so any unintended public-API change fires a build error. Track shipped surface in `PublicAPI.Shipped.txt`; require an `RS0017` shipped/unshipped diff in every PR.
- CI runs an API-compat check against the previously released package (e.g. `Microsoft.DotNet.ApiCompat.Tool`) before tagging a release.
- Major version bumps (`2.0`) are coordinated events — open a tracking issue, give companions a deprecation window, ship core and at least the first-party companions together.

**Companion convention:**

- Pin a *floor* matching the lowest core version you actually need. Don't chase the latest core just because it exists.
- Use `Directory.Packages.props` (central package management) to keep all `ZeroAlloc.*` floors in one place per repo.
- Let Renovate / Dependabot open PRs to bump floors. Merge those PRs only when there's a real reason (security, new API needed) — not on every release.

**No back-channels.** The core exposes only public contract types. Do not add `[InternalsVisibleTo]` for companions, do not stash extension points behind `internal` types, do not rely on undocumented runtime behavior. Anything a companion needs has to be a public, documented part of the contract — and therefore subject to the SemVer table above.
