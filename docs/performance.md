---
id: performance
title: Performance
sidebar_position: 4
---

# Performance

ZeroAlloc.Authorization is designed so that `IAuthorizationPolicy.EvaluateAsync` adds **zero heap allocation** on the happy path. That property is enforced at three layers — analyzers, an AOT smoke test, and a benchmark — so it cannot regress without one of them failing.

## Results

Measured 2026-10-17 with BenchmarkDotNet v0.15.8, Release configuration, `MemoryDiagnoser`
enabled, on an otherwise idle machine:

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.402
  [Host] : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
```

> Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published.

Each benchmark ran out of process in three launches, pinned to two performance cores
(`--affinity 20`). `Ratio` is each row's mean time relative to the baseline in the same run;
`Launch range` is the lowest and highest ratio of the three launches.

### Generated dispatch

[`GeneratedAuthorizerBenchmarks`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/blob/main/benchmarks/ZeroAlloc.Authorization.Benchmarks/GeneratedAuthorizerBenchmarks.cs)
calls the generator-emitted `AuthorizerFor<TRequest>.EvaluateAsync`, which is what a host calls.
The baseline is a request with a single `[RequirePolicy]`.

| Method | Ratio | Launch range | Allocated |
|---|---:|---:|---:|
| `[RequirePolicy]` | 1.00 | | 0 B |
| `[RequireAnyPolicy]`, first policy allows | 1.12 | 1.03–1.30 | 0 B |
| `[RequirePolicy("MinAge", 18)]`, parameterized | 0.91 | 0.85–1.04 | 0 B |

An any-of group costs about a tenth more than a single policy, and a parameterized policy costs the
same as a plain one within the launch spread. All three allocate nothing.

### A policy called directly

[`PolicyEvaluationBenchmarks`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/blob/main/benchmarks/ZeroAlloc.Authorization.Benchmarks/PolicyEvaluationBenchmarks.cs)
calls `IAuthorizationPolicy.EvaluateAsync` on a role-check policy, with no generated dispatch. It
has no baseline: **0 B** per call.

---

## Why the happy path allocates nothing

`EvaluateAsync` does a little work on every call, none of it on the heap:

1. **Struct construction.** `EvaluateAsync` instantiates a `UnitResult<AuthorizationFailure>` (which contains an `AuthorizationFailure` `readonly struct`) on every call. That's a value-typed write to the return slot, not a heap allocation.
2. **`ValueTask` wrap.** A sync-completing policy returns `new ValueTask<UnitResult<AuthorizationFailure>>(syncResult)`, which is also stack-only.
3. **The interface dispatch.** `((IAuthorizationPolicy)policy).EvaluateAsync(...)` goes through a virtual call. The JIT devirtualizes when the concrete type is known.

The whole pipeline stays zero-allocation because every type in the return chain is a value type and `AuthorizationFailure` is a `readonly struct`. This is the price of a structured, machine-readable deny over a bare `bool`, and it is paid in cycles, not in garbage.

---

## Three-line enforcement story

Zero-allocation is enforced as a contract, not a hope. Three independent gates fail loudly if any future change regresses it:

1. **Build-time analyzers.** `<IsAotCompatible>true</IsAotCompatible>` is set on the main library project, which enables the IL2026/IL3050 trim and AOT analyzers. Any reflection regression (boxed enum, `Type.GetMethods`, dynamic dispatch on a non-AOT-friendly path) surfaces as a build warning treated as an error.
2. **AOT smoke test in CI.** The `samples/ZeroAlloc.Authorization.AotSmoke/` console app is published with `PublishAot=true` on every CI run. It exercises `EvaluateAsync`, attribute reads, and anonymous-context construction. The job exits 0 only if the AOT publish succeeds and the binary executes without trimming-related runtime errors.
3. **Allocation gate tests.** [`AllocationBudgetTests.cs`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/blob/main/tests/ZeroAlloc.Authorization.Tests/AllocationBudgetTests.cs) measures the whole `EvaluateAsync` call with `AllocationGate` on every test run and asserts a 0-byte budget for the allow and deny paths, a parameterized policy and an any-of group. A future commit that introduces a closure, boxes an enum, or wraps a `Task` fails the test run. The benchmarks report the same 0 B, but they do not gate anything.

The combination is what makes "zero allocation" a durable property of the package rather than a one-time measurement.

---

## AOT compilation

The numbers above come from a JIT-compiled run. The smoke-test sample publishes the same hot path with NativeAOT on every CI run, to confirm there is no AOT-only regression hiding behind a reflection-on-startup quirk that the JIT happens to tolerate. In any real authorization decision the dominant cost is the work *inside* the policy (a database lookup, a claims service call), not the dispatch layer.

---

## Running the benchmarks

```bash
cd benchmarks/ZeroAlloc.Authorization.Benchmarks
dotnet run -c Release -- --filter "*" --launchCount 3
```

To run a specific benchmark:

```bash
dotnet run -c Release -- --filter "*Generated*"
```

---

## See also

- [Failure shape](core-concepts/failure-shape.md) — why `EvaluateAsync` returns `UnitResult<AuthorizationFailure>` instead of `bool`.
- [Sync vs async](core-concepts/sync-vs-async.md) — the async-only contract note and the sync-wrap idiom.
- [Policies](core-concepts/policies.md) — the single-method contract this benchmark exercises.
