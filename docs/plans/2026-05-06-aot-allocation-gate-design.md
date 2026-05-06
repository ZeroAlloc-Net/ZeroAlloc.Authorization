# AOT Allocation Gate — Design

**Date:** 2026-05-06
**Status:** Designed, ready for implementation plan
**Backlog item:** docs/backlog.md §6 — "Certify the ZeroAlloc promise"

## Problem

The README claims:

> Zero allocation on all four hot-path methods (`IsAuthorized`, `IsAuthorizedAsync`, `Evaluate`, `EvaluateAsync` happy path).

The package has the AOT badge, an `aot-smoke` CI job, and a `benchmarks/` project with `[MemoryDiagnoser]`. But **no consumer of those measurements ever fails CI on a regression.** A future change can quietly start allocating in any of the four APIs and nothing catches it.

The backlog calls this out:

> Add a `benchmarks/` project with BenchmarkDotNet runs over `IsAuthorized` and the `IsAuthorizedAsync` happy path. **Assert `Allocated == 0 B` for both.**

The "Assert" half is missing.

## Reframing: budgets, not zero

"Zero allocation" is the right contract for *this* package, but not org-wide. Different packages legitimately allocate by their nature (DI transient resolution, Outbox enqueue, etc.). The right shape is **per-API allocation budgets** that each package declares — and a gate that fails CI when the actual measurement exceeds the declared budget.

For Authorization v1, all four hot-path APIs declare 0 B. Easy budget table. The pattern lifts to other packages with non-zero budgets without changing the machinery.

## Architecture

A small measurement primitive (~40 LOC) brackets a function call with `GC.GetAllocatedBytesForCurrentThread()`, divides by iteration count, asserts the per-call allocation against a declared budget. It lives in two places that catch different regression classes:

1. **`tests/ZeroAlloc.Authorization.Tests/AllocationBudgetTests.cs`** — runs under JIT every `dotnet test`. Catches "someone added a closure or boxing in a happy-path method" before the PR merges.
2. **`samples/ZeroAlloc.Authorization.AotSmoke/Program.cs`** — extended to call the same bracket-and-assert logic on the AOT-published binary. Catches "trim/AOT changed escape analysis and now this allocates" — a class of regression the JIT test misses.

The two share a tiny helper (`AllocationGate`) that lives once in `samples/.../Internal/` and is included into the test project via `<Compile Include="...\samples\...\Internal\AllocationGate.cs" Link="Internal\AllocationGate.cs" />`. One source of truth, two consumers.

No new project, no new package, no CI changes. Existing `dotnet test` and `aot-smoke` jobs pick both up automatically.

## Components

### 1. `samples/ZeroAlloc.Authorization.AotSmoke/Internal/AllocationGate.cs` (new, ~40 LOC)

Single static helper class with two methods:

```csharp
internal static class AllocationGate
{
    public static void AssertBudget(int budgetBytes, int iterations, Action action, string label) { ... }
    public static void AssertBudgetValueTask<T>(int budgetBytes, int iterations, Func<ValueTask<T>> action, string label) { ... }
}
```

The async overload requires `IsCompletedSuccessfully` on every call and reads `.Result` — never schedules a continuation, so no awaiter machinery enters the measurement.

### 2. `samples/ZeroAlloc.Authorization.AotSmoke/Program.cs` (extended)

Today exercises behavior across all 4 APIs plus anonymous denial. Append a second pass that brackets the same calls with `AllocationGate.AssertBudget(0, 1000, ...)`. Existing prints stay; add a final `"AOT allocation gate OK"` line. On budget failure, the helper throws and the AOT-published binary exits non-zero — same failure shape as today's behavior assertions.

### 3. `tests/ZeroAlloc.Authorization.Tests/AllocationBudgetTests.cs` (new)

One xUnit `[Fact]` per measured API — 4 facts for the 4 APIs in the README's claim, plus 1 for anonymous denial. Each calls `AllocationGate.AssertBudget(...)`.

The test project's `.csproj` adds:

```xml
<ItemGroup>
  <Compile Include="..\..\samples\ZeroAlloc.Authorization.AotSmoke\Internal\AllocationGate.cs"
           Link="Internal\AllocationGate.cs" />
</ItemGroup>
```

Both projects compile against the same source.

## Measurement protocol

Inside `AllocationGate.AssertBudget`:

```
1. Run the action twice (warmup) — JIT-compiles, populates type-handle caches, allocates one-time fixtures.
2. GC.Collect() / WaitForPendingFinalizers / GC.Collect() — flush warmup garbage.
3. var before = GC.GetAllocatedBytesForCurrentThread()
4. for (int i = 0; i < iterations; i++) action()
5. var allocated = GC.GetAllocatedBytesForCurrentThread() - before
6. var perCall = allocated / iterations
7. if (perCall > budgetBytes) throw with label, perCall, budget in the message
```

**Why per-call, not total:** a single test-host `string.Format` slipping into the bracket inflates total but is rounded out by N=1000 calls. We measure steady-state cost.

**Why `iterations = 1000`:** large enough to make harness-noise rounding error negligible (a stray 24-byte allocation across 1000 calls reads as 0 B/call after integer division). Small enough that the test runs in <1 ms.

**Async overload:** every iteration calls
```csharp
var t = action();
if (!t.IsCompletedSuccessfully)
    throw new InvalidOperationException("...sync-completion-required...");
_ = t.Result;
```

This guarantees we never enter awaiter/state-machine machinery, which is what gives `ValueTask` its zero-alloc property.

## Budget table (Authorization v1)

| API | Iterations | Budget |
|---|---:|---:|
| `IsAuthorized` (allow) | 1000 | 0 B |
| `IsAuthorized` (deny via `AnonymousSecurityContext`) | 1000 | 0 B |
| `IsAuthorizedAsync` (allow) | 1000 | 0 B |
| `Evaluate` (allow) | 1000 | 0 B |
| `EvaluateAsync` (allow) | 1000 | 0 B |

## Failure shape

**Helper throws `InvalidOperationException`** with a message of the form:

```
AllocationGate: <label> allocated <perCall> B/call over <iterations> iterations
(total <allocated> B), budget is <budgetBytes> B/call.
Use BenchmarkDotNet [MemoryDiagnoser] locally to find the culprit.
```

The `label` carries the API name (`"IsAuthorized (allow)"`); the per-call number is what regressed; the hint points the next engineer at how to investigate. Plain `InvalidOperationException` (not a custom type) so the message is the contract — no consumer should catch this.

**xUnit consumer:** the helper throwing causes xUnit to report a failed test with the message; CI fails the `build` job. No bespoke assertion library, no swallowing.

**AOT smoke consumer:** the helper throws → unhandled exception unwinds top-of-program → AOT binary exits non-zero. Same failure path as today's behavior assertions.

**The helper doesn't catch anything** — underlying-call exceptions propagate too. If a regression makes `IsAuthorized` throw, the test reports the throw, not a budget violation. Behavior failure beats allocation failure (allocation only matters when the call works).

**One implicit failure path:** if AOT-compiled code accidentally schedules an async continuation, the async overload's `IsCompletedSuccessfully` check fires immediately with a clear message, before any allocation measurement. That's the "AOT regressed the sync-completion guarantee" failure separated from the "AOT changed allocation count" failure.

## Self-tests for the gate

The 5 budget tests serve as the positive control — if the helper had a measurement bug that always reported >0 B, none would pass.

Two negative-control tests live alongside the budget tests:

```csharp
[Fact]
public void Gate_DetectsAllocation_WhenActionAllocates()
{
    var ex = Assert.Throws<InvalidOperationException>(() =>
        AllocationGate.AssertBudget(0, 1000, () => _ = new object(), "test-allocator"));
    Assert.Contains("test-allocator", ex.Message);
    Assert.Contains("> 0 B", ex.Message);
}

[Fact]
public void Gate_RejectsValueTask_NotCompletedSynchronously()
{
    var ex = Assert.Throws<InvalidOperationException>(() =>
        AllocationGate.AssertBudgetValueTask(0, 1, async () => { await Task.Yield(); return 1; }, "yielding"));
    Assert.Contains("sync-completion-required", ex.Message);
}
```

They prove (a) the bracket math sees real allocations, and (b) the async overload's guard works. If either ever stops failing, the gate has degraded — the budget tests above could pass for the wrong reason.

## Out of scope

- **Org-wide rollout.** This design ships in Authorization first. The pattern (helper + linked source file in tests + extension of the AOT smoke binary) is deliberately repo-portable — sibling packages can adopt it by copying ~40 LOC and declaring their own budget table. No published `ZeroAlloc.Testing` package; no shared `tests/Common/` directory in the org.
- **BenchmarkDotNet integration.** The existing `benchmarks/` project keeps doing what it does (developer-facing perf measurement with `[MemoryDiagnoser]`). The CI gate is separate, lighter, and authoritative for regressions.
- **Annotations (`[RequiresDynamicCode]` / `[RequiresUnreferencedCode]`).** No API today needs them. Add only if a future API does.

## Versioning impact

None. No public API changes. Adding the gate is purely internal CI-side enforcement.
