---
id: performance
title: Performance
sidebar_position: 4
---

# Performance

ZeroAlloc.Authorization is designed so that `IAuthorizationPolicy.EvaluateAsync` adds **zero heap allocation** on the happy path. That property is enforced at three layers — analyzers, an AOT smoke test, and a benchmark — so it cannot regress without one of them failing.

## Results

BenchmarkDotNet (BDN ShortRun, .NET 10 release build, x64) — happy path on a simple role-check policy:

| Method | Mean | Allocated |
|---|---:|---:|
| `EvaluateAsync` | ~99 ns | 0 B |

These numbers are from BDN's ShortRun job — useful as indicative figures, but with wider confidence intervals than a full release-time run. The release-time benchmark uses BDN's default job; expect the mean to tighten and shift down marginally, but the `Allocated` column to stay at `0 B`.

Source: [`benchmarks/ZeroAlloc.Authorization.Benchmarks/PolicyEvaluationBenchmarks.cs`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/blob/main/benchmarks/ZeroAlloc.Authorization.Benchmarks/PolicyEvaluationBenchmarks.cs).

---

## Why ~99 ns for a one-method contract

The cycle count breaks down to:

1. **Struct construction.** `EvaluateAsync` instantiates a `UnitResult<AuthorizationFailure>` (which contains an `AuthorizationFailure` `readonly struct`) on every call. That's a value-typed write to the return slot, not a heap allocation, but it shows up in the cycle count.
2. **`ValueTask` wrap.** A sync-completing policy returns `new ValueTask<UnitResult<AuthorizationFailure>>(syncResult)` — also stack-only, also costs a few cycles.
3. **The interface dispatch.** `((IAuthorizationPolicy)policy).EvaluateAsync(...)` goes through a virtual call. The JIT devirtualizes when the concrete type is known.

The whole pipeline stays zero-allocation because every type in the return chain is a value type and `AuthorizationFailure` is a `readonly struct` — the work is on the stack. The cycle count is the price of a structured, machine-readable deny over a bare `bool`.

---

## Three-line enforcement story

Zero-allocation is enforced as a contract, not a hope. Three independent gates fail loudly if any future change regresses it:

1. **Build-time analyzers.** `<IsAotCompatible>true</IsAotCompatible>` is set on the main library project, which enables the IL2026/IL3050 trim and AOT analyzers. Any reflection regression (boxed enum, `Type.GetMethods`, dynamic dispatch on a non-AOT-friendly path) surfaces as a build warning treated as an error.
2. **AOT smoke test in CI.** The `samples/ZeroAlloc.Authorization.AotSmoke/` console app is published with `PublishAot=true` on every CI run. It exercises `EvaluateAsync`, attribute reads, and anonymous-context construction. The job exits 0 only if the AOT publish succeeds and the binary executes without trimming-related runtime errors.
3. **Benchmark project.** `PolicyEvaluationBenchmarks.cs` runs `EvaluateAsync` through BenchmarkDotNet's `[MemoryDiagnoser]`. The release-time benchmark asserts `Allocated == 0 B`. A future commit that introduces a closure, boxes an enum, or wraps a `Task` will surface non-zero `Allocated` and the gate fails.

The combination is what makes "zero allocation" a durable property of the package rather than a one-time measurement.

---

## AOT compilation

The numbers above come from a JIT-compiled run. Under AOT (NativeAOT publish), the same hot path is typically marginally faster — PGO-driven inlining and smaller code paths help — but the `Allocated` column stays at `0 B`. The smoke-test sample exists precisely to confirm there is no AOT-only allocation regression hiding behind a reflection-on-startup quirk that the JIT happens to tolerate.

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

- [Failure shape](core-concepts/failure-shape.md) — why `EvaluateAsync` returns `UnitResult<AuthorizationFailure>` instead of `bool`, and what the cycle delta buys.
- [Sync vs async](core-concepts/sync-vs-async.md) — the async-only contract note and the sync-wrap idiom.
- [Policies](core-concepts/policies.md) — the single-method contract this benchmark exercises.
