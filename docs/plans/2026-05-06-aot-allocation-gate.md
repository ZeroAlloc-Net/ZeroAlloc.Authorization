# AOT Allocation Gate Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a CI-enforceable allocation gate that fails the build if any of the four hot-path Authorization APIs (`IsAuthorized`, `IsAuthorizedAsync`, `Evaluate`, `EvaluateAsync`) regress to allocate on the happy path.

**Architecture:** A 40-LOC `AllocationGate` helper brackets calls with `GC.GetAllocatedBytesForCurrentThread()` and asserts a per-call budget. The helper lives once in the AOT smoke sample's `Internal/` and is included into the test project via `<Compile Include Link>`. Two consumers — the xUnit test project (JIT regression gate) and the AOT smoke binary (trim/AOT regression gate) — share the same source.

**Tech Stack:** .NET 10, xUnit 2.x, BCL `GC.GetAllocatedBytesForCurrentThread()`, Native AOT publish.

**Design doc:** [`2026-05-06-aot-allocation-gate-design.md`](2026-05-06-aot-allocation-gate-design.md)

---

## Working branch

The brainstorming step created `design/aot-allocation-gate` from `main` and committed the design doc on it. All implementation tasks below commit on top of that branch. Final task pushes and opens a PR.

---

## Task 1: AllocationGate sync overload + negative-control test

**Files:**
- Create: `samples/ZeroAlloc.Authorization.AotSmoke/Internal/AllocationGate.cs`
- Create: `tests/ZeroAlloc.Authorization.Tests/AllocationBudgetTests.cs`
- Modify: `tests/ZeroAlloc.Authorization.Tests/ZeroAlloc.Authorization.Tests.csproj`

**Step 1.1: Add `<Compile Include Link>` to test csproj**

Modify `tests/ZeroAlloc.Authorization.Tests/ZeroAlloc.Authorization.Tests.csproj`. Add this `<ItemGroup>` immediately before the closing `</Project>`:

```xml
<ItemGroup>
  <Compile Include="..\..\samples\ZeroAlloc.Authorization.AotSmoke\Internal\AllocationGate.cs"
           Link="Internal\AllocationGate.cs" />
</ItemGroup>
```

**Step 1.2: Create empty AllocationGate.cs (compiles, doesn't enforce)**

Create `samples/ZeroAlloc.Authorization.AotSmoke/Internal/AllocationGate.cs`:

```csharp
namespace ZeroAlloc.Authorization.AotSmoke.Internal;

internal static class AllocationGate
{
    public static void AssertBudget(int budgetBytes, int iterations, Action action, string label)
    {
        // Implementation pending in Step 1.4.
        for (int i = 0; i < iterations; i++) action();
    }
}
```

**Step 1.3: Write negative-control test (will fail because empty gate doesn't throw)**

Create `tests/ZeroAlloc.Authorization.Tests/AllocationBudgetTests.cs`:

```csharp
using ZeroAlloc.Authorization.AotSmoke.Internal;

namespace ZeroAlloc.Authorization.Tests;

public class AllocationBudgetTests
{
    [Fact]
    public void Gate_DetectsAllocation_WhenActionAllocates()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AllocationGate.AssertBudget(
                budgetBytes: 0,
                iterations: 1000,
                action: () => _ = new object(),
                label: "test-allocator"));

        Assert.Contains("test-allocator", ex.Message);
        Assert.Contains("budget is 0", ex.Message);
    }
}
```

**Step 1.4: Run test, expect FAIL**

Run from repo root:

```
dotnet test tests/ZeroAlloc.Authorization.Tests/ZeroAlloc.Authorization.Tests.csproj -c Release --filter "FullyQualifiedName~AllocationBudgetTests"
```

Expected: `Gate_DetectsAllocation_WhenActionAllocates` FAILS — the empty implementation doesn't throw, so `Assert.Throws<InvalidOperationException>` fails with "no exception thrown".

**Step 1.5: Implement the gate**

Replace the body of `AllocationGate.AssertBudget` in `samples/ZeroAlloc.Authorization.AotSmoke/Internal/AllocationGate.cs`:

```csharp
namespace ZeroAlloc.Authorization.AotSmoke.Internal;

internal static class AllocationGate
{
    public static void AssertBudget(int budgetBytes, int iterations, Action action, string label)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));

        // Warmup — JIT-compile, populate type-handle caches, allocate one-time fixtures.
        action();
        action();

        // Flush warmup garbage so it can't leak into the measurement.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) action();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var perCall = allocated / iterations;
        if (perCall > budgetBytes)
        {
            throw new InvalidOperationException(
                $"AllocationGate: {label} allocated {perCall} B/call over {iterations} iterations " +
                $"(total {allocated} B), budget is {budgetBytes} B/call. " +
                "Use BenchmarkDotNet [MemoryDiagnoser] locally to find the culprit.");
        }
    }
}
```

**Step 1.6: Run test, expect PASS**

```
dotnet test tests/ZeroAlloc.Authorization.Tests/ZeroAlloc.Authorization.Tests.csproj -c Release --filter "FullyQualifiedName~AllocationBudgetTests"
```

Expected: `Gate_DetectsAllocation_WhenActionAllocates` PASSES. The exception message contains both `test-allocator` and `budget is 0`.

**Step 1.7: Commit**

```
git add samples/ZeroAlloc.Authorization.AotSmoke/Internal/AllocationGate.cs \
        tests/ZeroAlloc.Authorization.Tests/AllocationBudgetTests.cs \
        tests/ZeroAlloc.Authorization.Tests/ZeroAlloc.Authorization.Tests.csproj
git commit -m "test(gate): allocation budget helper with sync overload"
```

---

## Task 2: AsyncoverloadFor ValueTask

**Files:**
- Modify: `samples/ZeroAlloc.Authorization.AotSmoke/Internal/AllocationGate.cs`
- Modify: `tests/ZeroAlloc.Authorization.Tests/AllocationBudgetTests.cs`

**Step 2.1: Write negative-control test (will compile-fail — method doesn't exist yet)**

Append to `tests/ZeroAlloc.Authorization.Tests/AllocationBudgetTests.cs`:

```csharp
[Fact]
public void Gate_RejectsValueTask_NotCompletedSynchronously()
{
    var ex = Assert.Throws<InvalidOperationException>(() =>
        AllocationGate.AssertBudgetValueTask<int>(
            budgetBytes: 0,
            iterations: 1,
            action: async () => { await Task.Yield(); return 1; },
            label: "yielding"));

    Assert.Contains("sync-completion-required", ex.Message);
}
```

**Step 2.2: Run test, expect compile FAIL**

```
dotnet build tests/ZeroAlloc.Authorization.Tests/ZeroAlloc.Authorization.Tests.csproj -c Release
```

Expected: build fails with `CS0117: 'AllocationGate' does not contain a definition for 'AssertBudgetValueTask'`.

**Step 2.3: Add the async overload**

Append inside the `AllocationGate` class in `samples/ZeroAlloc.Authorization.AotSmoke/Internal/AllocationGate.cs`:

```csharp
public static void AssertBudgetValueTask<T>(int budgetBytes, int iterations, Func<ValueTask<T>> action, string label)
{
    ArgumentNullException.ThrowIfNull(action);
    if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));

    static T Drain(ValueTask<T> t)
    {
        if (!t.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException(
                "AllocationGate: sync-completion-required — the supplied ValueTask did not " +
                "complete synchronously. Awaiter machinery would pollute the measurement; " +
                "the API under test must return an already-completed ValueTask.");
        }
        return t.Result;
    }

    // Warmup.
    Drain(action());
    Drain(action());

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < iterations; i++) Drain(action());
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

    var perCall = allocated / iterations;
    if (perCall > budgetBytes)
    {
        throw new InvalidOperationException(
            $"AllocationGate: {label} allocated {perCall} B/call over {iterations} iterations " +
            $"(total {allocated} B), budget is {budgetBytes} B/call. " +
            "Use BenchmarkDotNet [MemoryDiagnoser] locally to find the culprit.");
    }
}
```

**Step 2.4: Run tests, expect PASS**

```
dotnet test tests/ZeroAlloc.Authorization.Tests/ZeroAlloc.Authorization.Tests.csproj -c Release --filter "FullyQualifiedName~AllocationBudgetTests"
```

Expected: both negative-control tests pass.

**Step 2.5: Commit**

```
git add samples/ZeroAlloc.Authorization.AotSmoke/Internal/AllocationGate.cs \
        tests/ZeroAlloc.Authorization.Tests/AllocationBudgetTests.cs
git commit -m "test(gate): allocation budget helper async overload"
```

---

## Task 3: Budget tests for the four hot-path APIs

**Files:**
- Modify: `tests/ZeroAlloc.Authorization.Tests/AllocationBudgetTests.cs`

**Step 3.1: Add the 5 budget tests**

Append to `tests/ZeroAlloc.Authorization.Tests/AllocationBudgetTests.cs`. Note the inner `AdminOnlyPolicy` and `TestContext` types match the patterns already used in the existing tests:

```csharp
[Fact]
public void IsAuthorized_AllowPath_ZeroAllocation()
{
    IAuthorizationPolicy policy = new AdminOnlyPolicy();
    var ctx = NewAdminContext();
    AllocationGate.AssertBudget(0, 1000, () => _ = policy.IsAuthorized(ctx), "IsAuthorized (allow)");
}

[Fact]
public void IsAuthorized_DenyAnonymous_ZeroAllocation()
{
    IAuthorizationPolicy policy = new AdminOnlyPolicy();
    AllocationGate.AssertBudget(0, 1000,
        () => _ = policy.IsAuthorized(AnonymousSecurityContext.Instance),
        "IsAuthorized (deny anonymous)");
}

[Fact]
public void Evaluate_AllowPath_ZeroAllocation()
{
    IAuthorizationPolicy policy = new AdminOnlyPolicy();
    var ctx = NewAdminContext();
    AllocationGate.AssertBudget(0, 1000, () => _ = policy.Evaluate(ctx), "Evaluate (allow)");
}

[Fact]
public void IsAuthorizedAsync_AllowPath_ZeroAllocation()
{
    IAuthorizationPolicy policy = new AdminOnlyPolicy();
    var ctx = NewAdminContext();
    AllocationGate.AssertBudgetValueTask(0, 1000, () => policy.IsAuthorizedAsync(ctx), "IsAuthorizedAsync (allow)");
}

[Fact]
public void EvaluateAsync_AllowPath_ZeroAllocation()
{
    IAuthorizationPolicy policy = new AdminOnlyPolicy();
    var ctx = NewAdminContext();
    AllocationGate.AssertBudgetValueTask(0, 1000, () => policy.EvaluateAsync(ctx), "EvaluateAsync (allow)");
}

private static TestContext NewAdminContext()
    => new("alice", new HashSet<string> { "Admin" }, new Dictionary<string, string>());

private sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
}

private sealed record TestContext(string Id,
                                  IReadOnlySet<string> Roles,
                                  IReadOnlyDictionary<string, string> Claims) : ISecurityContext;
```

Add `using ZeroAlloc.Authorization;` at the top of the file if not already present (it isn't).

**Step 3.2: Run all tests, expect PASS**

```
dotnet test tests/ZeroAlloc.Authorization.Tests/ZeroAlloc.Authorization.Tests.csproj -c Release --filter "FullyQualifiedName~AllocationBudgetTests"
```

Expected: 7 passing tests (2 negative-controls + 5 budget tests).

If any of the 5 budget tests fails, the contract claim in the README is wrong — investigate before proceeding. The two async tests are most at risk: confirm the default-interface implementations of `IsAuthorizedAsync` / `EvaluateAsync` return `ValueTask.FromResult(...)` over a synchronously-completed sync call. If they return a fresh `Task`-backed `ValueTask`, this is a real bug worth fixing before claiming the gate works.

**Step 3.3: Commit**

```
git add tests/ZeroAlloc.Authorization.Tests/AllocationBudgetTests.cs
git commit -m "test(gate): budget tests for IsAuthorized/Evaluate happy-paths"
```

---

## Task 4: Extend AOT smoke binary with the same gate

**Files:**
- Modify: `samples/ZeroAlloc.Authorization.AotSmoke/Program.cs`

**Step 4.1: Append gate calls to Program.cs**

Modify `samples/ZeroAlloc.Authorization.AotSmoke/Program.cs`. After the existing behavior assertions and before `Console.WriteLine("AOT smoke OK");`, insert:

```csharp
// Allocation budget gate — run AFTER behavior assertions so behavior failures are reported first.
// Asserts the README's "Zero allocation on all four hot-path methods" claim under the
// AOT runtime (catches trim/escape-analysis regressions the JIT-side test misses).
AllocationGate.AssertBudget(0, 1000, () => _ = policy.IsAuthorized(ctx), "IsAuthorized (allow)");
AllocationGate.AssertBudget(0, 1000,
    () => _ = policy.IsAuthorized(AnonymousSecurityContext.Instance),
    "IsAuthorized (deny anonymous)");
AllocationGate.AssertBudget(0, 1000, () => _ = ((IAuthorizationPolicy)policy).Evaluate(ctx), "Evaluate (allow)");
AllocationGate.AssertBudgetValueTask(0, 1000, () => ((IAuthorizationPolicy)policy).IsAuthorizedAsync(ctx), "IsAuthorizedAsync (allow)");
AllocationGate.AssertBudgetValueTask(0, 1000, () => ((IAuthorizationPolicy)policy).EvaluateAsync(ctx), "EvaluateAsync (allow)");

Console.WriteLine("AOT allocation gate OK");
```

Add `using ZeroAlloc.Authorization.AotSmoke.Internal;` at the top of `Program.cs`. (Top-level statements share file scope with the helper namespace; the `using` is required because the helper is `internal` in its namespace.)

**Step 4.2: Build the AOT smoke project (JIT-side first to catch syntax issues fast)**

```
dotnet build samples/ZeroAlloc.Authorization.AotSmoke/ZeroAlloc.Authorization.AotSmoke.csproj -c Release
```

Expected: build succeeds with 0 warnings (the csproj has `WarningsAsErrors=IL2026/IL2067/IL2075/IL2091/IL3050/IL3051` so any AOT trim warning fails the build).

**Step 4.3: Run the JIT-built smoke locally**

```
dotnet run --project samples/ZeroAlloc.Authorization.AotSmoke/ZeroAlloc.Authorization.AotSmoke.csproj -c Release
```

Expected output ends with:

```
AOT smoke OK
AOT allocation gate OK
```

Exit code 0.

If the output stops at `AOT smoke OK` and exits non-zero, an `InvalidOperationException` from the gate is being thrown; investigate the label printed in the error.

**Step 4.4: Optional — run the AOT-published smoke (Linux-equivalent test)**

If on Windows, this step typically runs only in CI. If you want to run it locally:

```
dotnet publish samples/ZeroAlloc.Authorization.AotSmoke/ZeroAlloc.Authorization.AotSmoke.csproj -r win-x64 -c Release -o ./aot-out
./aot-out/ZeroAlloc.Authorization.AotSmoke.exe
```

(Linux: `-r linux-x64`, no `.exe` suffix. Requires `clang` and `zlib1g-dev`; CI runner installs these via the existing `aot-smoke` job.)

Expected: same output as Step 4.3.

**Step 4.5: Commit**

```
git add samples/ZeroAlloc.Authorization.AotSmoke/Program.cs
git commit -m "feat(certify): wire allocation gate into AOT smoke binary"
```

---

## Task 5: Push, open PR, watch CI

**Step 5.1: Push the branch**

```
git push -u origin design/aot-allocation-gate
```

**Step 5.2: Open the PR**

```bash
gh pr create --base main --head design/aot-allocation-gate \
  --title "feat(certify): AOT-enforced allocation gate for happy-path APIs" \
  --body "$(cat <<'EOF'
## Summary

Adds a CI-enforceable allocation gate that fails the build if any of the four hot-path Authorization APIs (`IsAuthorized`, `IsAuthorizedAsync`, `Evaluate`, `EvaluateAsync`) regress to allocate on the happy path. Closes the `Assert Allocated == 0 B` half of backlog item §6 (the certification infrastructure — `IsAotCompatible`, `aot-smoke` job, AOT badge — was already in place).

## Architecture

A 40-LOC `AllocationGate` helper brackets calls with `GC.GetAllocatedBytesForCurrentThread()` and asserts a per-call budget. Lives once in `samples/.../Internal/AllocationGate.cs`, included into the test project via `<Compile Include Link>`. Two consumers share the source:

- **`tests/AllocationBudgetTests.cs`** — JIT-side gate, runs every `dotnet test`. Catches "someone added a closure or boxing" before merge.
- **`samples/AotSmoke/Program.cs`** — AOT-side gate. Catches "trim/escape analysis changed and now this allocates" — a class of regression the JIT test misses.

Reframed from "0 B everywhere" to **per-API budget table**: Authorization v1 declares 0 B for all four hot-path methods, but the same machinery lifts to sibling packages with non-zero budgets without changing the helper.

## Self-tests

The 5 budget tests serve as the positive control. Two negative-control tests guard the helper itself:

- `Gate_DetectsAllocation_WhenActionAllocates` — proves the bracket math sees real allocations.
- `Gate_RejectsValueTask_NotCompletedSynchronously` — proves the async overload's sync-completion guard fires.

If either ever stops failing, the gate has degraded; the budget tests above could pass for the wrong reason.

## Design doc

[`docs/plans/2026-05-06-aot-allocation-gate-design.md`](docs/plans/2026-05-06-aot-allocation-gate-design.md)

## Test plan

- [ ] CI `build` job: 7 new test cases pass under JIT.
- [ ] CI `aot-smoke` job: AOT-published binary prints `AOT allocation gate OK` and exits 0.
- [ ] CI `api-compat` job: green (no public API changes).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**Step 5.3: Watch CI**

```
gh pr checks --watch
```

Expected: `lint-commits`, `build`, `aot-smoke`, `api-compat` all green.

If `build` fails on a budget test: the README's zero-alloc claim is wrong for that API on this commit. Investigate before merging.

If `aot-smoke` fails specifically (and `build` passes): the AOT runtime allocates where the JIT runtime doesn't. Investigate via `dotnet publish ... -r linux-x64` locally on a Linux box (or via WSL) and inspect the failure label.

---

## Done

After merge:
- The README's "Zero allocation on all four hot-path methods" claim is enforced — no more aspirational allocation-free.
- The pattern (~40 LOC helper + linked compile + AOT smoke extension) is repo-portable. Sibling packages (Mediator, Cache, Resilience, etc.) can adopt it by copying the helper, declaring their own budget tables, and extending their `aot-smoke` samples. The Authorization repo serves as the reference implementation.
- Backlog item §6 closes — the "Certify the ZeroAlloc promise" certification is now enforceable rather than aspirational.
