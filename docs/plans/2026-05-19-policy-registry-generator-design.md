# Policy Registry Generator — Design

**Date:** 2026-05-19
**Status:** Brainstormed and approved; ready for implementation plan
**Tracks:** ZA.Authorization backlog #5 — "Source-generated policy registry on the contract side"
**Graduation signal:** Fired 2026-05-06 by Mediator PR #74 (Mediator.Authorization shipped host-side generator)
**Versioning impact:** ZA.Authorization v1.2.2 → v2.0.0; ZA.Mediator.Authorization v4.1.0 → v5.0.0 (coupled release)

## Context

ZA.Authorization currently ships only the runtime contract (`IAuthorizationPolicy`, `[AuthorizationPolicy("name")]`, `[Authorize("name")]`, `ISecurityContext`, `AuthorizationFailure`). The downstream `ZeroAlloc.Mediator.Authorization` package owns the source generator that discovers policies and emits the lookup registry hosts consume.

This means the generator + ~80 LOC of host-side plumbing (`LookupEmitter`, `PolicyDiscovery`, `RequestDiscovery`, `MediatorAuthorizationGeneratedHooks` static state, `ValidatePoliciesAreRegistered` eager check) live in the wrong repo. The fired graduation signal: Mediator.Authorization v4.1.0 already proved the generator pattern works. The pattern now belongs in the contract repo so any future framework (not just Mediator) can consume it identically.

## Goal

Lift the source generator into `ZeroAlloc.Authorization` v2 so it becomes the single canonical place for policy discovery and dispatcher generation. Downstream `Mediator.Authorization` v5 shrinks to a thin runtime behavior consuming `AuthorizerFor<TRequest>` via DI generic dispatch — the same pattern `Mediator.Validation` uses.

Net effect: ~150 LOC deleted across the org, one canonical pattern for "decorate, generate, DI-resolve" matching the rest of the ZA family.

## Design decisions

Nine decisions locked during brainstorming. Each is recorded with its chosen option, the rejected alternatives, and the rationale.

### D1 — Consolidation scope: full (~150 LOC deletion)

`ZeroAlloc.Authorization.Generator` (bundled into the main package) owns ALL generator logic: policy discovery, request discovery, dispatcher emission, DI registration emission. `Mediator.Authorization`'s generator project is deleted in full; its runtime `AuthorizationBehavior` shrinks to `sp.GetService<AuthorizerFor<TRequest>>()?.EvaluateAsync(...)`.

Rejected: half-consolidation (policy half only, leaving request mapping in Mediator.Authorization). The duplication-removal payoff is the whole point; half measures leave drift between two repos that share an attribute namespace.

### D2 — Generated API surface: typed dispatcher per request, no name lookup

Generator emits one `AuthorizerFor<TRequest>` subclass per `[RequirePolicy]`-decorated request. The runtime contract is purely typed dispatch — no `Resolve(string name)` API survives.

```csharp
internal sealed class GeneratedAuthorizerFor_DeleteUserCommand(IServiceProvider sp)
    : AuthorizerFor<DeleteUserCommand>
{
    public override async ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
    {
        var p = sp.GetRequiredService<AdminPolicy>();
        return await p.EvaluateAsync(ctx, ct);
    }
}
```

Rejected: name-based lookup (`GeneratedAuthorizationLookup.Resolve(name)`) for ad-hoc consumers. Direct `sp.GetRequiredService<AdminPolicy>()` already works for that use case since `[Policy]`-decorated classes self-register.

### D3 — Attribute target: class-level only

`[RequirePolicy]` applies to types only (`AttributeTargets.Class | AttributeTargets.Struct`). Method-level usage triggers ZAUTH005 (compile error).

Rejected: keeping method-level for hypothetical MVC integration. No fired graduation signal for that use case. Easy to add in v2.x when concrete requirements arrive.

### D4 — Naming: short, MS-collision-conscious renames

| Old (v1.x) | New (v2.0) | Reason |
|---|---|---|
| `[AuthorizationPolicy("admin")]` | `[Policy("admin")]` | Symmetric pair with the request side, shorter at every declaration site |
| `[Authorize("admin")]` | `[RequirePolicy("admin")]` | Eliminates collision with `Microsoft.AspNetCore.Authorization.AuthorizeAttribute` (household-name overlap was causing real-world `using` ambiguity) |

Both attributes live in the `ZeroAlloc.Authorization` namespace. Short names match the rest of the ZA family (`[Handler]`, `[Validate]`, `[ValueObject]`, `[Map]`, `[Retry]`, `[Get]`, `[Inject]`, `[Saga]`, `[StateMachine]`) — no `ZeroAlloc`-prefixed names anywhere in the org.

Rejected: `[ZeroAllocAuthorizationPolicy]` and similar prefixed names. The `[Authorize]` collision was unique because of household-name overlap with ASP.NET. `[Policy]` and `[RequirePolicy]` are not used by Microsoft frameworks at the attribute level (`Microsoft.AspNetCore.Authorization` exposes `Policy` as a *property* on `[Authorize]`, not as a standalone attribute).

### D5 — Package layout: bundled, never split

Single `ZeroAlloc.Authorization` NuGet package containing both the runtime contract and the generator-as-analyzer. Consumers reference one package; no standalone `ZeroAlloc.Authorization.Generator` is ever published.

Rejected: standalone Generator package (matches ZA.Mediator's pattern). Following the post-PR-#101 ZA.Rest direction. Day-one bundled-only means we never need a ZR9001-style dual-reference diagnostic — there's nothing to back-compat against.

### D6 — Discovery scope: current compilation + referenced assemblies

Generator walks `Compilation.SourceModule` for in-project policies AND iterates `Compilation.SourceModule.ReferencedAssemblySymbols` for cross-assembly policies. The shared-kernel pattern (policies in `MyApp.SharedKernel`, requests in `MyApp.Api`) works out of the box.

Rejected: current-compilation only (forces policy duplication per project), opt-in cross-assembly via `[assembly: ScanForPolicies(typeof(Marker))]` (more ceremony for the common case).

### D7 — Diagnostic set: five compile-time errors

| Code | Trigger |
|---|---|
| ZAUTH001 | `[RequirePolicy("X")]` with no matching `[Policy("X")]` in compilation + references |
| ZAUTH002 | Two `[Policy("X")]` declarations with the same name |
| ZAUTH003 | `[Policy]`-decorated class doesn't implement `IAuthorizationPolicy` |
| ZAUTH004 | `[Policy]` class is abstract / static / non-instantiable via DI |
| ZAUTH005 | `[RequirePolicy]` applied to non-class type (interface / delegate / primitive) |

All five are errors (build failures). No warnings in v1 — orphan-policy detection (declared `[Policy]` with no `[RequirePolicy]` reference) was considered and rejected as false-positive-prone (direct `sp.GetRequiredService<MyPolicy>()` consumers are legitimate).

### D8 — DI lifetimes: scoped across the board

- `[Policy]`-decorated classes: `AddScoped<TPolicy>()`
- `AuthorizerFor<TRequest>` implementations: `AddScoped<AuthorizerFor<TRequest>, GeneratedAuthorizerFor_TRequest>()`

Matches Mediator.Validation's pattern. Per-request instantiation is negligible compared to the actual policy evaluation cost. Singleton was considered and rejected: capturing `IServiceProvider` in a singleton breaks scoped resolution; the perf "win" is imaginary at this layer.

### D9 — Interface: async-only

```csharp
public interface IAuthorizationPolicy
{
    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default);
}
```

Drops the four-method matrix (sync `IsAuthorized` + async, sync `Evaluate` + async). Sync-completing policies wrap their result in `new ValueTask<...>(syncResult)` — allocation-free, JIT-friendly. Matches modern .NET async-first conventions.

## Architecture

ZA.Authorization v2 package (one NuGet, bundled):

```
ZeroAlloc.Authorization/
├── src/ZeroAlloc.Authorization/
│   ├── PolicyAttribute.cs              (renamed; AttributeTargets.Class)
│   ├── RequirePolicyAttribute.cs       (renamed; AttributeTargets.Class only)
│   ├── IAuthorizationPolicy.cs         (single async method)
│   ├── ISecurityContext.cs             (unchanged)
│   ├── AuthorizationFailure.cs         (unchanged)
│   ├── AuthorizerFor.cs                (NEW abstract base)
│   └── AuthorizationServiceCollectionExtensions.cs (NEW partial; generator fills in AddZeroAllocAuthorization)
└── src/ZeroAlloc.Authorization.Generator/
    ├── PolicyRegistryGenerator.cs      (IIncrementalGenerator entry point)
    ├── Discovery/PolicySymbolWalker.cs (cross-assembly policy lookup)
    ├── Discovery/RequireSymbolWalker.cs (cross-assembly request lookup)
    ├── Emit/AuthorizerForEmitter.cs    (one subclass per [RequirePolicy])
    ├── Emit/DIRegistrationEmitter.cs   (AddZeroAllocAuthorization extension)
    └── Diagnostics/ZAUTH001-005 descriptor definitions
```

`Mediator.Authorization` v5 (drastically smaller):

```
ZeroAlloc.Mediator.Authorization/
├── src/ZeroAlloc.Mediator.Authorization/
│   ├── AuthorizationBehavior.cs        (~20 LOC — resolves AuthorizerFor<T>, calls EvaluateAsync)
│   └── ServiceCollectionExtensions.cs  (~10 LOC — registers behavior)
└── DELETED:
    ├── src/ZeroAlloc.Mediator.Authorization.Generator/  (entire project)
    ├── MediatorAuthorizationGeneratedHooks.cs           (~50 LOC static delegate state)
    └── ValidatePoliciesAreRegistered logic              (~20 LOC eager DI check)
```

## Data flow

**Build time:**

1. `PolicyRegistryGenerator` runs in consumer's compilation.
2. `PolicySymbolWalker` finds all `[Policy("name")]`-decorated classes (compilation + references).
3. `RequireSymbolWalker` finds all `[RequirePolicy("name")]`-decorated types.
4. Cross-validation runs (ZAUTH001-005).
5. `AuthorizerForEmitter` writes one `GeneratedAuthorizerFor_<RequestName>` per `[RequirePolicy]` target.
6. `DIRegistrationEmitter` writes `AddZeroAllocAuthorization(this IServiceCollection)` extension.
7. Single `ZeroAllocAuthorization.g.cs` lands in consumer's `obj/`.

**Startup:**

```csharp
services.AddZeroAllocAuthorization();    // generated — registers policies + AuthorizerFor<T>'s
services.AddMediator();
services.AddMediatorAuthorization();     // registers AuthorizationBehavior pipeline behavior
```

**Per-request dispatch:**

```
IMediator.Send(new DeleteUserCommand(...), ct)
  ↓
AuthorizationBehavior<DeleteUserCommand, Unit>.HandleAsync
  ↓
sp.GetService<AuthorizerFor<DeleteUserCommand>>()  → GeneratedAuthorizerFor_DeleteUserCommand
  ↓
authorizer.EvaluateAsync(ctx, ct)
  ↓
sp.GetRequiredService<AdminPolicy>().EvaluateAsync(ctx, ct)  → UnitResult.Success / Failure
  ↓
IsFailure?  throw AuthorizationException(failure)  :  await next()
```

**Allocation profile (happy path):** 0 bytes. DI lookups are cache hits, `ValueTask` wraps sync results on the stack, no string keys at runtime, no reflection.

## Error handling

**Compile-time:** ZAUTH001-005 errors (above) prevent code that would runtime-fail.

**Runtime — auth failure:** `EvaluateAsync` returns `UnitResult.Failure(...)`; `AuthorizationBehavior` throws `AuthorizationException(failure)` carrying `Code` + `Reason`. Host translates to HTTP 403.

**Runtime — DI mis-registration:** `GetRequiredService<TPolicy>` throws `InvalidOperationException` with the policy type name. Better than the v1 eager-validation throw at startup — same information, only paid for at the point of failure.

**Runtime — generator crashed at build time (`RS1xxx`):** `sp.GetService<AuthorizerFor<T>>()` returns `null`. `AuthorizationBehavior` skips the check (fail-open, matches `Mediator.Validation`). Trade-off: prefers liveness over safety for the edge case of broken codegen. The alternative (fail-closed) was rejected — punishes consumers who legitimately have requests without `[RequirePolicy]`.

## AOT story

- All generated code is statically-typed `GetRequiredService<TConcrete>()` — no reflection, no `Activator.CreateInstance`, no `Expression.Compile`.
- `AuthorizerFor<TRequest>` open generic registered with closed-type implementations (no open-generic DI resolution at runtime, which is the AOT-incompatible pattern).
- Existing `ZeroAlloc.Authorization.AotSmoke` binary (shipped by Authorization #6) gains one new scenario: `[RequirePolicy]` dispatching through a `[Policy]` evaluator. Asserts ≤ 0 bytes on the happy-path Evaluate.
- Trimming: no `[DynamicallyAccessedMembers]` annotations needed. All references are static; trimmer keeps what's reachable through DI registration, which the generated extension methods name explicitly.

## Testing strategy

| Layer | What's tested | Tooling |
|---|---|---|
| Generator snapshots | Emitted code matches committed `.verified.cs` | Verify.SourceGenerators |
| Diagnostic positive/negative | Each ZAUTH00N has trigger + clean-source case | `Microsoft.CodeAnalysis.CSharp.Testing` |
| Cross-assembly | Policy in LibA, `[RequirePolicy]` in LibB referencing LibA | In-memory compilation via `MetadataReference.CreateFromImage` |
| Incremental cache | Roslyn pipeline reruns cleanly when unrelated files change | `GeneratorDriver.RunGeneratorsAndUpdateCompilation` snapshots |
| Runtime contract | `IAuthorizationPolicy` async-only contract; sync-completing path has zero alloc | xUnit + ZA memory benchmark gate |
| AOT smoke | `PublishAot=true` build runs the new `[RequirePolicy]` scenario | Existing `ZeroAlloc.Authorization.AotSmoke` harness |
| Mediator integration | `AuthorizerFor<T>` registered → behavior calls it; not registered → skip (fail-open); failure → `AuthorizationException` | Mediator repo's `ZeroAlloc.Mediator.Authorization.Tests` |

**CI gates for v2 to ship:**
- All generator + runtime + integration tests pass
- AOT smoke binary shows 0 alloc on the new scenario
- `PublicAPI.Shipped.txt` diff intentional (breaking changes acknowledged)
- `dotnet pack` produces ZA.Authorization 2.0 and ZA.Mediator.Authorization 5.0 with matching dependency floors

## Migration & versioning

**ZA.Authorization v1.2.2 → v2.0.0 — breaking changes:**

- `[AuthorizationPolicy("admin")]` → `[Policy("admin")]` (mechanical find/replace)
- `[Authorize("admin")]` → `[RequirePolicy("admin")]` (mechanical find/replace)
- `IAuthorizationPolicy` reduced to single `EvaluateAsync`. Sync-only policies wrap result in `new ValueTask<...>(syncResult)`.
- Method-level `[Authorize]` (if any consumer had it) must move to the containing class (ZAUTH005).
- Name-lookup helpers (`Resolve(string)`, `GetPoliciesFor<T>()`) removed.

**ZA.Mediator.Authorization v4.1.0 → v5.0.0 — coupled release:**

- Generator project deleted entirely.
- Static `MediatorAuthorizationGeneratedHooks` state deleted.
- `AuthorizationBehavior` rewritten (~70 LOC → ~20 LOC).
- `WithAuthorization()` builder loses `AutoRegisterDiscoveredPolicies` flag (always on) and eager `ValidatePoliciesAreRegistered` (DI handles it).
- Pins `ZeroAlloc.Authorization >= 2.0.0`.

**Release sequencing:**

1. ZA.Authorization v2.0.0 ships first. Standalone — non-Mediator consumers can adopt immediately.
2. ZA.Mediator.Authorization v5.0.0 ships second, pinning the new floor.
3. ZA.Mediator core (4.1.0) does NOT bump — only the Authorization extension changes.

**No deprecation overlap.** v2 is a clean break — one breaking version takes the pain once. Maintaining a v1.x compatibility shim alongside v2 would gut the consolidation benefit.

**Consumer migration example:**

```csharp
// Before (v1.x):
[AuthorizationPolicy("admin")]
public sealed class AdminPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
}

[Authorize("admin")]
public sealed record DeleteUser(...) : IRequest<Unit>;

// After (v2.0):
[Policy("admin")]
public sealed class AdminPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx.Roles.Contains("Admin")
            ? UnitResult.Success<AuthorizationFailure>()
            : UnitResult.Failure(new AuthorizationFailure("policy.deny", "Admin role required")));
}

[RequirePolicy("admin")]
public sealed record DeleteUser(...) : IRequest<Unit>;

// Program.cs — one line added before MediatorAuthorization:
+ services.AddZeroAllocAuthorization();
  services.AddMediatorAuthorization();
```

## Out of scope (deferred to v2.x)

- Method-level `[RequirePolicy]` for MVC/Minimal API integration. Add when a concrete consumer arrives with requirements.
- Name-based `Resolve(string)` lookup for ad-hoc consumers. Direct `sp.GetRequiredService<TPolicy>()` covers this use case.
- Runtime policy composition (`[RequirePolicy("admin", LogicalOperator.Or, "owner")]`). Backlog item Authorization #1.
- Resource-based authorization (`IAuthorizationPolicy<TResource>`). Backlog item Authorization #3.
- Parameterized policies (`[Policy<TParam>]`). Backlog item Authorization #2.

Each is additive in v2.x without further breaking changes.
