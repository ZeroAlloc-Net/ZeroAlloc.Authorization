---
id: performance
title: Performance
sidebar_position: 4
---

# Performance

ZeroAlloc.Authorization is designed so that every entry point on `IAuthorizationPolicy` adds **zero heap allocation** on the happy path. That property is enforced at three layers — analyzers, an AOT smoke test, and a benchmark — so it cannot regress without one of them failing.

## Results

BenchmarkDotNet (BDN ShortRun, .NET 10 release build, x64) — happy path on a simple role-check policy:

| Method | Mean | Allocated |
|---|---:|---:|
| `IsAuthorized` | ~9 ns | 0 B |
| `IsAuthorizedAsync` | ~31 ns | 0 B |
| `Evaluate` | ~7 ns | 0 B |
| `EvaluateAsync` | ~99 ns | 0 B |

These numbers are from BDN's ShortRun job — useful as indicative figures, but with wider confidence intervals than a full release-time run. The release-time benchmark uses BDN's default job; expect the means to tighten and shift down marginally, but the `Allocated` column to stay at `0 B` across the board.

Source: [`benchmarks/ZeroAlloc.Authorization.Benchmarks/PolicyEvaluationBenchmarks.cs`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/blob/main/benchmarks/ZeroAlloc.Authorization.Benchmarks/PolicyEvaluationBenchmarks.cs).

---

## Why `EvaluateAsync` is ~99 ns vs `IsAuthorizedAsync`'s ~31 ns

It is **not** allocation — both are 0 B. The cost difference is two things:

1. **Extra struct construction.** `EvaluateAsync` instantiates a `UnitResult<AuthorizationFailure>` (which contains an `AuthorizationFailure` `readonly struct`) on every call. That's a value-typed write to the return slot, not a heap allocation, but it shows up in the cycle count.
2. **A slightly heavier async state machine.** The default `EvaluateAsync` wraps `IsAuthorizedAsync` and then projects the bool through a `UnitResult` — one more state-machine step than `IsAuthorizedAsync` itself.

Both stay zero-allocation because `UnitResult<TError>` is a value type and `AuthorizationFailure` is a `readonly struct` — the work is on the stack. The cycle delta is the price of a coded deny over a bare `bool`.

`Evaluate` (sync, ~7 ns) is the fastest because it skips the async machinery entirely and the inlined struct construction folds away under JIT.

---

## Three-line enforcement story

Zero-allocation is enforced as a contract, not a hope. Three independent gates fail loudly if any future change regresses it:

1. **Build-time analyzers.** `<IsAotCompatible>true</IsAotCompatible>` is set on the main library project, which enables the IL2026/IL3050 trim and AOT analyzers. Any reflection regression (boxed enum, `Type.GetMethods`, dynamic dispatch on a non-AOT-friendly path) surfaces as a build warning treated as an error.
2. **AOT smoke test in CI.** The `samples/ZeroAlloc.Authorization.AotSmoke/` console app is published with `PublishAot=true` on every CI run. It exercises every contract entry point — `IsAuthorized`, `IsAuthorizedAsync`, `Evaluate`, `EvaluateAsync`, attribute reads, anonymous-context construction. The job exits 0 only if the AOT publish succeeds and the binary executes without trimming-related runtime errors.
3. **Benchmark project.** `PolicyEvaluationBenchmarks.cs` runs the four hot-path methods through BenchmarkDotNet's `[MemoryDiagnoser]`. The release-time benchmark asserts `Allocated == 0 B` for every entry point. A future commit that introduces a closure, boxes an enum, or wraps a `Task` will surface non-zero `Allocated` and the gate fails.

The combination is what makes "zero allocation" a durable property of the package rather than a one-time measurement.

---

## AOT compilation

The numbers above come from a JIT-compiled run. Under AOT (NativeAOT publish), the same hot paths are typically marginally faster — PGO-driven inlining and smaller code paths help — but the `Allocated` column stays at `0 B`. The smoke-test sample exists precisely to confirm there is no AOT-only allocation regression hiding behind a reflection-on-startup quirk that the JIT happens to tolerate.

If you publish your host with AOT, expect the policy-evaluation overhead to be a few nanoseconds tighter than the table above. That difference is rarely material — the dominant cost in any real authorization decision is the work *inside* the policy (a database lookup, a claims service call), not the dispatch layer.

---

## Running the benchmarks

```bash
cd benchmarks/ZeroAlloc.Authorization.Benchmarks
dotnet run -c Release
```

To run a specific benchmark:

```bash
dotnet run -c Release --filter "*Evaluate*"
```

---

## See also

- [Failure shape](core-concepts/failure-shape.md) — why `Evaluate` exists alongside `IsAuthorized`, and why the cycle delta is the price of a coded deny.
- [Sync vs async](core-concepts/sync-vs-async.md) — when each entry point is the right one.
- [Policies](core-concepts/policies.md) — the four-method contract these benchmarks exercise.
