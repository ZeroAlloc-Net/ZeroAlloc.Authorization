# ZeroAlloc.Authorization 1.0 Readiness — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship `ZeroAlloc.Authorization 1.0` with the contract surface frozen under SemVer, AOT/zero-alloc certification, public-API gates, and integration into the docs site / org profile.

**Architecture:** Five workstreams. (1) Add `AuthorizationFailure` + `Evaluate`/`EvaluateAsync` via DIM additive change — depends on `ZeroAlloc.Results`. (2) Wire `IsAotCompatible`, AOT smoke sample, BenchmarkDotNet harness. (3) Add `PublicApiAnalyzers` + ApiCompat CI gate. (4) Align CI/build infrastructure with `ZeroAlloc.Resilience` (donor). (5) Author docs content + scaffold website app + submodule + org profile entry.

**Tech Stack:** .NET 8/9/10, C# 12, Microsoft.CodeAnalysis.PublicApiAnalyzers, Microsoft.DotNet.ApiCompat.Tool, BenchmarkDotNet, ZeroAlloc.Results, GitHub Actions, Docusaurus 3.9, pnpm/turbo, Cloudflare Workers.

**Reference design:** [docs/plans/2026-05-01-v1-readiness-design.md](2026-05-01-v1-readiness-design.md)

**Donor for copy-paste:** `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Resilience` (CI, AOT smoke, benchmarks, trigger-website).

**Tooling assumptions:**
- Tests use the existing `tests/ZeroAlloc.Authorization.Tests` project (xUnit; verify framework on first task).
- All commits follow Conventional Commits — `feat:`, `fix:`, `chore:`, `docs:`, `test:`, `refactor:`, `build:`, `ci:`.
- This plan executes on branch `feat/v1-readiness` off `main` (create on Task 0).
- Repo currently has no `origin` remote — Task 0 also wires it before any push.

---

## Task 0: Bootstrap branch + remote

**Files:** none — git plumbing only.

**Step 1: Verify current state**

Run: `cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization && git branch --show-current && git status --short`

Expected: clean working tree on either `main` or `docs/v1-readiness-design`.

**Step 2: Wire the `origin` remote (one-time)**

Run: `git remote add origin https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization.git && git fetch origin main`

Expected: fetch succeeds; `origin/main` ref exists.

**Step 3: Create the implementation branch off `origin/main`**

Run: `git switch -c feat/v1-readiness origin/main`

Expected: switched to branch `feat/v1-readiness`.

**Step 4: Verify test framework**

Run: `cat tests/ZeroAlloc.Authorization.Tests/ZeroAlloc.Authorization.Tests.csproj | grep -E "(Microsoft.NET.Test|xunit|nunit|MSTest)"`

Expected: identifies test framework. Plan assumes xUnit; if NUnit/MSTest, adjust assertion syntax in subsequent tasks.

**No commit** — bootstrap only.

---

# Workstream 1 — `AuthorizationFailure` + `Evaluate`/`EvaluateAsync`

## Task 1.1: Add `ZeroAlloc.Results` package reference

**Files:**
- Modify: `src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj`

**Step 1: Add the PackageReference**

Edit the `<ItemGroup>` (or add one) to include:

```xml
<PackageReference Include="ZeroAlloc.Results" Version="0.1.4" />
```

**Step 2: Restore + build to verify the package resolves**

Run: `dotnet restore src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj && dotnet build src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj -c Release`

Expected: build succeeds; `UnitResult<T>` resolvable from `ZeroAlloc.Results` namespace.

**Step 3: Commit**

```bash
git add src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj
git commit -m "build: add ZeroAlloc.Results 0.1.4 dependency for v1 contract"
```

---

## Task 1.2: Create `AuthorizationFailure` struct (TDD)

**Files:**
- Create: `src/ZeroAlloc.Authorization/AuthorizationFailure.cs`
- Test: `tests/ZeroAlloc.Authorization.Tests/AuthorizationFailureTests.cs`

**Step 1: Write the failing tests**

Create `tests/ZeroAlloc.Authorization.Tests/AuthorizationFailureTests.cs`:

```csharp
namespace ZeroAlloc.Authorization.Tests;

public class AuthorizationFailureTests
{
    [Fact]
    public void Constructor_StoresCodeAndReason()
    {
        var f = new AuthorizationFailure("policy.deny.role", "user is not Admin");
        Assert.Equal("policy.deny.role", f.Code);
        Assert.Equal("user is not Admin", f.Reason);
    }

    [Fact]
    public void Constructor_AllowsNullReason()
    {
        var f = new AuthorizationFailure("policy.deny");
        Assert.Equal("policy.deny", f.Code);
        Assert.Null(f.Reason);
    }

    [Fact]
    public void Constructor_RejectsNullCode()
    {
        Assert.Throws<ArgumentNullException>(() => new AuthorizationFailure(null!));
    }

    [Fact]
    public void Constructor_RejectsEmptyCode()
    {
        Assert.Throws<ArgumentException>(() => new AuthorizationFailure(""));
    }

    [Fact]
    public void DefaultDenyCode_IsStableConstant()
    {
        Assert.Equal("policy.deny", AuthorizationFailure.DefaultDenyCode);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Authorization.Tests --filter "FullyQualifiedName~AuthorizationFailureTests"`

Expected: compile error (`AuthorizationFailure` does not exist).

**Step 3: Write minimal implementation**

Create `src/ZeroAlloc.Authorization/AuthorizationFailure.cs`:

```csharp
namespace ZeroAlloc.Authorization;

/// <summary>Structured deny information returned from <see cref="IAuthorizationPolicy.Evaluate"/>.
/// Hosts surface <see cref="Code"/> for machine-readable matching and <see cref="Reason"/> for
/// optional human-readable text.</summary>
public readonly struct AuthorizationFailure
{
    /// <summary>Default deny code emitted when an <c>IsAuthorized=false</c> result is wrapped
    /// without a more specific code. Hosts can match on this to detect "policy denied without
    /// explanation" vs an explicitly-coded deny.</summary>
    public const string DefaultDenyCode = "policy.deny";

    /// <summary>Machine-readable code, e.g. "policy.deny.role" or "tenant.inactive".
    /// Non-null, non-empty.</summary>
    public string Code { get; }

    /// <summary>Optional human-readable reason. Hosts may surface this in API responses
    /// or logs; treat as untrusted-for-display unless the policy author guarantees it.</summary>
    public string? Reason { get; }

    public AuthorizationFailure(string code, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        Code = code;
        Reason = reason;
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Authorization.Tests --filter "FullyQualifiedName~AuthorizationFailureTests"`

Expected: 5/5 pass.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Authorization/AuthorizationFailure.cs tests/ZeroAlloc.Authorization.Tests/AuthorizationFailureTests.cs
git commit -m "feat: add AuthorizationFailure struct for structured deny info"
```

---

## Task 1.3: Add `Evaluate` + `EvaluateAsync` to `IAuthorizationPolicy` (TDD)

**Files:**
- Modify: `src/ZeroAlloc.Authorization/IAuthorizationPolicy.cs`
- Test: `tests/ZeroAlloc.Authorization.Tests/AuthorizationPolicyEvaluateTests.cs`

**Step 1: Write the failing tests**

Create `tests/ZeroAlloc.Authorization.Tests/AuthorizationPolicyEvaluateTests.cs`:

```csharp
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization.Tests;

public class AuthorizationPolicyEvaluateTests
{
    private static readonly TestContext Ctx = new("alice",
        new HashSet<string> { "Admin" },
        new Dictionary<string, string>());

    [Fact]
    public void Evaluate_DefaultsToWrapping_IsAuthorized_True()
    {
        var policy = new AllowingPolicy();
        var result = policy.Evaluate(Ctx);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Evaluate_DefaultsToWrapping_IsAuthorized_False_WithDefaultDenyCode()
    {
        var policy = new DenyingPolicy();
        var result = policy.Evaluate(Ctx);
        Assert.False(result.IsSuccess);
        Assert.Equal(AuthorizationFailure.DefaultDenyCode, result.Error.Code);
        Assert.Null(result.Error.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_DefaultsToWrapping_IsAuthorizedAsync_True()
    {
        var policy = new AllowingPolicy();
        var result = await policy.EvaluateAsync(Ctx);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EvaluateAsync_DefaultsToWrapping_IsAuthorizedAsync_False_WithDefaultDenyCode()
    {
        var policy = new DenyingPolicy();
        var result = await policy.EvaluateAsync(Ctx);
        Assert.False(result.IsSuccess);
        Assert.Equal(AuthorizationFailure.DefaultDenyCode, result.Error.Code);
    }

    [Fact]
    public void Evaluate_OverrideEmitsCustomCode()
    {
        var policy = new RichDenyPolicy();
        var result = policy.Evaluate(Ctx);
        Assert.False(result.IsSuccess);
        Assert.Equal("policy.deny.role", result.Error.Code);
        Assert.Equal("user is not Admin", result.Error.Reason);
    }

    private sealed class AllowingPolicy : IAuthorizationPolicy
    {
        public bool IsAuthorized(ISecurityContext ctx) => true;
    }

    private sealed class DenyingPolicy : IAuthorizationPolicy
    {
        public bool IsAuthorized(ISecurityContext ctx) => false;
    }

    private sealed class RichDenyPolicy : IAuthorizationPolicy
    {
        public bool IsAuthorized(ISecurityContext ctx) => false;
        public UnitResult<AuthorizationFailure> Evaluate(ISecurityContext ctx)
            => new AuthorizationFailure("policy.deny.role", "user is not Admin");
    }

    private sealed record TestContext(string Id,
                                      IReadOnlySet<string> Roles,
                                      IReadOnlyDictionary<string, string> Claims) : ISecurityContext;
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Authorization.Tests --filter "FullyQualifiedName~AuthorizationPolicyEvaluateTests"`

Expected: compile errors (`Evaluate` and `EvaluateAsync` not on `IAuthorizationPolicy`).

**Step 3: Add the new methods to the interface with DIM defaults**

Edit `src/ZeroAlloc.Authorization/IAuthorizationPolicy.cs`:

```csharp
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization;

public interface IAuthorizationPolicy
{
    bool IsAuthorized(ISecurityContext ctx);

    ValueTask<bool> IsAuthorizedAsync(ISecurityContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(IsAuthorized(ctx));
    }

    /// <summary>Structured-result sync evaluation. Default wraps <see cref="IsAuthorized"/>.
    /// Override to return a richer deny code/reason.</summary>
    UnitResult<AuthorizationFailure> Evaluate(ISecurityContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return IsAuthorized(ctx)
            ? UnitResult.Success<AuthorizationFailure>()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode);
    }

    /// <summary>Structured-result async evaluation. Default wraps <see cref="IsAuthorizedAsync"/>.</summary>
    async ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ct.ThrowIfCancellationRequested();
        return await IsAuthorizedAsync(ctx, ct).ConfigureAwait(false)
            ? UnitResult.Success<AuthorizationFailure>()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode);
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Authorization.Tests`

Expected: all tests pass — including the existing `AuthorizationPolicyAsyncTests` and `AttributeTests` (DIM is purely additive, nothing should regress).

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Authorization/IAuthorizationPolicy.cs tests/ZeroAlloc.Authorization.Tests/AuthorizationPolicyEvaluateTests.cs
git commit -m "feat: add Evaluate/EvaluateAsync via UnitResult<AuthorizationFailure>"
```

---

# Workstream 2 — AOT certification + benchmarks

## Task 2.1: Set `IsAotCompatible=true` + trim/single-file analyzers

**Files:**
- Modify: `src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj`

**Step 1: Add the three properties**

Inside the existing `<PropertyGroup>`:

```xml
<IsAotCompatible>true</IsAotCompatible>
<EnableTrimAnalyzer>true</EnableTrimAnalyzer>
<EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
```

**Step 2: Build and verify no IL2026 / IL3050 fires**

Run: `dotnet build src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj -c Release`

Expected: 0 warnings (no reflection or dynamic-code patterns in the contract). If any fire, **stop and surface to the user** — that's a real regression in the contract code.

**Step 3: Commit**

```bash
git add src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj
git commit -m "build: enable IsAotCompatible + trim/single-file analyzers"
```

---

## Task 2.2: Scaffold `samples/ZeroAlloc.Authorization.AotSmoke/` project

**Files:**
- Create: `samples/ZeroAlloc.Authorization.AotSmoke/ZeroAlloc.Authorization.AotSmoke.csproj`
- Reference donor: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Resilience/samples/ZeroAlloc.Resilience.AotSmoke/`

**Step 1: Read the donor csproj**

Run: `cat c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Resilience/samples/ZeroAlloc.Resilience.AotSmoke/ZeroAlloc.Resilience.AotSmoke.csproj`

Note its property values; reuse them.

**Step 2: Create the new csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <IsPackable>false</IsPackable>
    <RootNamespace>ZeroAlloc.Authorization.AotSmoke</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ZeroAlloc.Authorization\ZeroAlloc.Authorization.csproj" />
  </ItemGroup>
</Project>
```

(If the donor uses different property names — e.g. `<RootNamespace>` is named differently — match the donor.)

**Step 3: Verify path layout**

Run: `ls samples/ZeroAlloc.Authorization.AotSmoke/`

Expected: contains the new csproj.

**Step 4: Commit**

```bash
git add samples/ZeroAlloc.Authorization.AotSmoke/ZeroAlloc.Authorization.AotSmoke.csproj
git commit -m "build: scaffold AOT smoke-test sample project"
```

---

## Task 2.3: Write the AOT smoke `Program.cs`

**Files:**
- Create: `samples/ZeroAlloc.Authorization.AotSmoke/Program.cs`

**Step 1: Write the program**

```csharp
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;

var ctx = new TestContext("alice",
    new HashSet<string> { "Admin" },
    new Dictionary<string, string>());
var policy = new AdminOnlyPolicy();

if (!policy.IsAuthorized(ctx)) throw new("IsAuthorized regressed");
if (!policy.Evaluate(ctx).IsSuccess) throw new("Evaluate regressed");
if (!await policy.IsAuthorizedAsync(ctx)) throw new("IsAuthorizedAsync regressed");
if (!(await policy.EvaluateAsync(ctx)).IsSuccess) throw new("EvaluateAsync regressed");

if (policy.IsAuthorized(AnonymousSecurityContext.Instance))
    throw new("Anonymous should be denied");

Console.WriteLine("AOT smoke OK");
return 0;

[AuthorizationPolicy("AdminOnly")]
sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
}

sealed record TestContext(string Id,
                          IReadOnlySet<string> Roles,
                          IReadOnlyDictionary<string, string> Claims) : ISecurityContext;
```

**Step 2: Verify it compiles (non-AOT first)**

Run: `dotnet build samples/ZeroAlloc.Authorization.AotSmoke/ZeroAlloc.Authorization.AotSmoke.csproj -c Release`

Expected: builds clean.

**Step 3: Run it (non-AOT) to verify logic**

Run: `dotnet run --project samples/ZeroAlloc.Authorization.AotSmoke -c Release`

Expected: prints `AOT smoke OK`, exits 0.

**Step 4: Commit**

```bash
git add samples/ZeroAlloc.Authorization.AotSmoke/Program.cs
git commit -m "feat: AOT smoke test exercises every public path"
```

---

## Task 2.4: Verify AOT publish works locally

**Files:** none — verification only.

**Step 1: Publish with `PublishAot=true`**

Run (Linux/macOS): `dotnet publish samples/ZeroAlloc.Authorization.AotSmoke -r linux-x64 -c Release -o ./aot-out`

Run (Windows): `dotnet publish samples/ZeroAlloc.Authorization.AotSmoke -r win-x64 -c Release -o ./aot-out`

Expected: ILC succeeds with **0 IL2026/IL3050 warnings**. If any fire, stop and surface to user.

**Step 2: Run the published binary**

Run (Linux/macOS): `./aot-out/ZeroAlloc.Authorization.AotSmoke`

Run (Windows): `./aot-out/ZeroAlloc.Authorization.AotSmoke.exe`

Expected: prints `AOT smoke OK`, exits 0.

**Step 3: Clean up**

Run: `rm -rf ./aot-out` (Linux/macOS) or equivalent.

**No commit** — verification step. If failed, return to earlier tasks to fix.

---

## Task 2.5: Add `samples/ZeroAlloc.Authorization.AotSmoke` to `.slnx`

**Files:**
- Modify: `ZeroAlloc.Authorization.slnx`

**Step 1: Inspect the .slnx**

Run: `cat ZeroAlloc.Authorization.slnx`

Note the structure (slnx is XML).

**Step 2: Add the project entry**

Add a `<Project>` element matching the existing pattern, pointing to `samples/ZeroAlloc.Authorization.AotSmoke/ZeroAlloc.Authorization.AotSmoke.csproj`.

**Step 3: Verify the solution still loads**

Run: `dotnet build ZeroAlloc.Authorization.slnx -c Release`

Expected: builds without missing-project errors.

**Step 4: Commit**

```bash
git add ZeroAlloc.Authorization.slnx
git commit -m "build: add AotSmoke sample to solution"
```

---

## Task 2.6: Add `aot-smoke` job to `ci.yml`

**Files:**
- Modify: `.github/workflows/ci.yml`
- Reference donor: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Resilience/.github/workflows/ci.yml` (`aot-smoke` job)

**Step 1: Copy the donor job**

From the donor file, copy the entire `aot-smoke:` job block. Paste into Authorization's `ci.yml` after the `build:` job. Replace project paths:

- `samples/ZeroAlloc.Resilience.AotSmoke/...` → `samples/ZeroAlloc.Authorization.AotSmoke/...`
- Output binary name: `ZeroAlloc.Resilience.AotSmoke` → `ZeroAlloc.Authorization.AotSmoke`

**Step 2: Lint the YAML**

Run: `cat .github/workflows/ci.yml | head -100` — visually inspect indentation.

Optional: if `yamllint` is available, `yamllint .github/workflows/ci.yml`.

**Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add aot-smoke job copied from Resilience"
```

---

## Task 2.7: Scaffold `benchmarks/ZeroAlloc.Authorization.Benchmarks/`

**Files:**
- Create: `benchmarks/ZeroAlloc.Authorization.Benchmarks/ZeroAlloc.Authorization.Benchmarks.csproj`
- Reference donor: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Resilience/benchmarks/ZeroAlloc.Resilience.Benchmarks/`

**Step 1: Read the donor csproj**

Run: `cat c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Resilience/benchmarks/ZeroAlloc.Resilience.Benchmarks/ZeroAlloc.Resilience.Benchmarks.csproj`

**Step 2: Create the new csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>ZeroAlloc.Authorization.Benchmarks</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="<version-from-donor>" />
    <ProjectReference Include="..\..\src\ZeroAlloc.Authorization\ZeroAlloc.Authorization.csproj" />
  </ItemGroup>
</Project>
```

Match `BenchmarkDotNet` version from the donor.

**Step 3: Commit**

```bash
git add benchmarks/ZeroAlloc.Authorization.Benchmarks/ZeroAlloc.Authorization.Benchmarks.csproj
git commit -m "build: scaffold benchmarks project"
```

---

## Task 2.8: Write `PolicyEvaluationBenchmarks`

**Files:**
- Create: `benchmarks/ZeroAlloc.Authorization.Benchmarks/Program.cs`
- Create: `benchmarks/ZeroAlloc.Authorization.Benchmarks/PolicyEvaluationBenchmarks.cs`

**Step 1: Program.cs**

```csharp
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
```

**Step 2: PolicyEvaluationBenchmarks.cs**

```csharp
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class PolicyEvaluationBenchmarks
{
    private readonly AdminOnlyPolicy _policy = new();
    private readonly TestContext _ctx = new("alice",
        new HashSet<string> { "Admin" },
        new Dictionary<string, string>());

    [Benchmark]
    public bool IsAuthorized() => _policy.IsAuthorized(_ctx);

    [Benchmark]
    public async ValueTask<bool> IsAuthorizedAsync()
        => await _policy.IsAuthorizedAsync(_ctx);

    [Benchmark]
    public UnitResult<AuthorizationFailure> Evaluate() => _policy.Evaluate(_ctx);

    [Benchmark]
    public async ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync()
        => await _policy.EvaluateAsync(_ctx);

    private sealed class AdminOnlyPolicy : IAuthorizationPolicy
    {
        public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
    }

    private sealed record TestContext(string Id,
                                      IReadOnlySet<string> Roles,
                                      IReadOnlyDictionary<string, string> Claims) : ISecurityContext;
}
```

**Step 3: Build**

Run: `dotnet build benchmarks/ZeroAlloc.Authorization.Benchmarks -c Release`

Expected: builds clean.

**Step 4: Add to `.slnx`**

Edit `ZeroAlloc.Authorization.slnx` to include the new project.

Run: `dotnet build ZeroAlloc.Authorization.slnx -c Release`

Expected: solution builds.

**Step 5: Commit**

```bash
git add benchmarks/ZeroAlloc.Authorization.Benchmarks/ ZeroAlloc.Authorization.slnx
git commit -m "feat: add PolicyEvaluationBenchmarks (BenchmarkDotNet harness)"
```

---

## Task 2.9: Run benchmarks locally; verify 0 B happy path

**Files:** none — verification only.

**Step 1: Run all benchmarks**

Run: `dotnet run --project benchmarks/ZeroAlloc.Authorization.Benchmarks -c Release -- --filter "*"`

(BenchmarkDotNet must run in Release config.)

**Step 2: Inspect the report**

Expected output table includes `Allocated` column. For all four methods (`IsAuthorized`, `IsAuthorizedAsync`, `Evaluate`, `EvaluateAsync`) the Allocated column must read `-` or `0 B`.

If any row shows non-zero allocation, **stop and surface to user** — there's a hidden allocation in the contract code.

**Step 3: Save the report excerpt**

Copy the relevant rows of the report — they'll go into `docs/performance.md` (Task 5.12) and the README (Task 2.10).

**No commit** — verification step.

---

## Task 2.10: Add AOT badge + benchmarks excerpt to README

**Files:**
- Modify: `README.md`

**Step 1: Add the AOT badge**

Insert after the existing badges:

```markdown
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
```

**Step 2: Add a "Performance" section before "License"**

```markdown
## Performance

BenchmarkDotNet ([source](benchmarks/ZeroAlloc.Authorization.Benchmarks/PolicyEvaluationBenchmarks.cs)) — happy path on a simple role-check policy:

| Method | Mean | Allocated |
|---|---:|---:|
| `IsAuthorized` | <fill from Task 2.9> | 0 B |
| `IsAuthorizedAsync` | <fill from Task 2.9> | 0 B |
| `Evaluate` | <fill from Task 2.9> | 0 B |
| `EvaluateAsync` | <fill from Task 2.9> | 0 B |
```

(Replace `<fill from Task 2.9>` with the measured Mean values.)

**Step 3: Commit**

```bash
git add README.md
git commit -m "docs: add AOT badge and benchmark numbers"
```

---

# Workstream 3 — Engineering gates

## Task 3.1: Add `Microsoft.CodeAnalysis.PublicApiAnalyzers` + AdditionalFiles

**Files:**
- Modify: `src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj`
- Create: `src/ZeroAlloc.Authorization/PublicAPI.Shipped.txt` (empty for now)
- Create: `src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt` (empty for now)

**Step 1: Add package + AdditionalFiles**

In the csproj:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" Version="3.11.0">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
</ItemGroup>

<ItemGroup>
  <AdditionalFiles Include="PublicAPI.Shipped.txt" />
  <AdditionalFiles Include="PublicAPI.Unshipped.txt" />
</ItemGroup>
```

**Step 2: Create the empty tracking files**

Run: `touch src/ZeroAlloc.Authorization/PublicAPI.Shipped.txt src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt`

**Step 3: Build to surface every undeclared public symbol**

Run: `dotnet build src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj -c Release`

Expected: large number of `RS0016` warnings — one per public symbol not yet recorded. **This is correct.**

**Step 4: Commit**

```bash
git add src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj src/ZeroAlloc.Authorization/PublicAPI.Shipped.txt src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt
git commit -m "build: add PublicApiAnalyzers + tracking files (baseline TBD)"
```

---

## Task 3.2: Generate the public-API baseline

**Files:**
- Modify: `src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt`

**Step 1: Use the analyzer's code-fix or generate manually**

In your IDE (VS / Rider / VSCode), open the project. Each `RS0016` warning has a quick-fix "Add to public API". Apply it to all of them. The fix populates `PublicAPI.Unshipped.txt`.

**Alternative (CLI):** examine the build's diagnostic output and write each undeclared symbol manually to `PublicAPI.Unshipped.txt`. Format:

```
ZeroAlloc.Authorization.AnonymousSecurityContext
ZeroAlloc.Authorization.AnonymousSecurityContext.AnonymousSecurityContext() -> void
ZeroAlloc.Authorization.AnonymousSecurityContext.Claims.get -> System.Collections.Generic.IReadOnlyDictionary<string!, string!>!
... (every public symbol, alphabetical, one per line)
```

**Step 2: Verify build now has 0 RS0016 warnings**

Run: `dotnet build src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj -c Release`

Expected: 0 warnings, 0 errors.

**Step 3: Commit**

```bash
git add src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt
git commit -m "build: populate PublicAPI.Unshipped.txt baseline"
```

---

## Task 3.3: Set `WarningsAsErrors` for RS0016/RS0017

**Files:**
- Modify: `Directory.Build.props`

**Step 1: Inspect current props**

Run: `cat Directory.Build.props`

**Step 2: Add or extend `<WarningsAsErrors>`**

Inside a `<PropertyGroup>`:

```xml
<WarningsAsErrors>$(WarningsAsErrors);RS0016;RS0017</WarningsAsErrors>
```

If `<WarningsAsErrors>` already exists, extend it; do not overwrite.

**Step 3: Verify clean build**

Run: `dotnet build -c Release`

Expected: 0 errors, 0 warnings.

**Step 4: Commit**

```bash
git add Directory.Build.props
git commit -m "build: treat RS0016/RS0017 as errors (lock public surface)"
```

---

## Task 3.4: Add `api-compat` CI job

**Files:**
- Modify: `.github/workflows/ci.yml`

**Step 1: Append the api-compat job**

After the `aot-smoke` job, add:

```yaml
  api-compat:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - name: Install ApiCompat tool
        run: dotnet tool install --global Microsoft.DotNet.ApiCompat.Tool
      - name: Build current
        run: dotnet build src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj -c Release
      - name: Compare against latest published 1.x
        run: |
          latest=$(curl -s https://api.nuget.org/v3-flatcontainer/zeroalloc.authorization/index.json \
                   | jq -r '.versions[]' | grep -E '^1\.' | tail -1)
          if [ -z "$latest" ]; then
            echo "No published 1.x yet — first 1.0 release. Skipping baseline check."
            exit 0
          fi
          nuget install ZeroAlloc.Authorization -Version "$latest" -OutputDirectory ./baseline
          apicompat package \
            --left ./baseline/ZeroAlloc.Authorization.$latest/lib/net10.0/ZeroAlloc.Authorization.dll \
            --right src/ZeroAlloc.Authorization/bin/Release/net10.0/ZeroAlloc.Authorization.dll
```

**Step 2: Lint YAML**

Visual inspect; if `yamllint` available, run it.

**Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add api-compat job (no-op until 1.0 publishes)"
```

---

# Workstream 4 — Pipeline alignment

## Task 4.1: Audit `Directory.Build.props` vs Resilience donor

**Files:**
- Modify (possibly): `Directory.Build.props`

**Step 1: Diff against donor**

Run: `diff Directory.Build.props c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Resilience/Directory.Build.props`

**Step 2: For each missing property in Authorization, decide**

Donor properties to check: `LangVersion=latest`, `Nullable=enable`, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`. Add to Authorization any that are missing **and** that the donor's pattern shows we want.

**Step 3: Verify clean build after each addition**

Run: `dotnet build -c Release` after each property addition.

If any addition triggers a warning-as-error, stop and surface to user — that's a real code-quality gap to fix before the build can stay clean.

**Step 4: Commit**

```bash
git add Directory.Build.props
git commit -m "build: align Directory.Build.props with rest of family"
```

---

## Task 4.2: Add `trigger-website.yml` workflow

**Files:**
- Create: `.github/workflows/trigger-website.yml`
- Reference donor: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Resilience/.github/workflows/trigger-website.yml`

**Step 1: Copy verbatim**

Run: `cp c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Resilience/.github/workflows/trigger-website.yml .github/workflows/trigger-website.yml`

The donor is already a template (the comment header says "copy this file"). No edits needed.

**Step 2: Verify content**

Run: `cat .github/workflows/trigger-website.yml`

Expected: dispatches `submodule-update` to `ZeroAlloc-Net/.website` on `docs/**` push to `main`.

**Step 3: Commit**

```bash
git add .github/workflows/trigger-website.yml
git commit -m "ci: add trigger-website workflow for docs/** changes"
```

**Step 4: Note for the user (no action in this plan)**

A `WEBSITE_DISPATCH_TOKEN` secret must be added to the repo settings on GitHub (fine-grained PAT with `Actions: write` on `ZeroAlloc-Net/.website`). Document this in the plan completion summary.

---

## Task 4.3: Audit `publish.yml` vs `release-please.yml` for redundancy

**Files:**
- Possibly: `.github/workflows/publish.yml`

**Step 1: Diff the two**

Run: `diff .github/workflows/publish.yml .github/workflows/release-please.yml`

**Step 2: Decide**

- If `publish.yml` is a manual-trigger publish that complements `release-please.yml`'s automated publish — keep both, document.
- If they overlap (both publish to nuget.org on the same trigger), one should be deleted.
- If `publish.yml` predates `release-please.yml` adoption, delete `publish.yml`.

**Step 3 (if delete):** `git rm .github/workflows/publish.yml`

**Step 4: Commit**

```bash
git add -A .github/workflows/
git commit -m "ci: remove redundant publish.yml (release-please handles it)"
```

(Or if kept, no commit needed — surface decision in the completion summary.)

---

## Task 4.4: Verify `renovate.json` exists; copy from donor if missing

**Files:**
- Possibly create: `renovate.json`

**Step 1: Check existence**

Run: `ls renovate.json 2>/dev/null || echo "MISSING"`

**Step 2: If missing, copy from donor**

Run: `cp c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Resilience/renovate.json renovate.json`

**Step 3: Inspect content for any Resilience-specific fields**

Run: `cat renovate.json` — verify nothing references `Resilience` by name. If it does, fix.

**Step 4: Commit (only if added)**

```bash
git add renovate.json
git commit -m "build: align renovate config with rest of family"
```

---

# Workstream 5 — Docs site integration

## Task 5.1: Author `docs/index.md` (landing page)

**Files:**
- Create: `docs/index.md`

**Step 1: Write the landing page**

Brief, links to other sections. Mirror the structure of [ZeroAlloc.Resilience/docs/index.md](c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Resilience/docs/index.md) for tone/format.

Content covers: what this package is, what it isn't (no host — needs an integration), one-line code sample, quick links.

**Step 2: Commit**

```bash
git add docs/index.md
git commit -m "docs: add docs site landing page"
```

---

## Task 5.2: Author `docs/getting-started.md`

**Files:**
- Create: `docs/getting-started.md`

**Step 1: Write the getting-started guide**

Cover: install, write a first policy, attach `[Authorize]` to a method, host integration prerequisite (the host wires the dispatch — point at AI.Sentinel and Mediator.Authorization).

**Step 2: Commit**

```bash
git add docs/getting-started.md
git commit -m "docs: add getting-started guide"
```

---

## Task 5.3-5.7: Author `docs/core-concepts/*.md`

Five separate atomic tasks — one per file. Each task: write the file, commit.

| File | Topic |
|---|---|
| `docs/core-concepts/security-context.md` | `ISecurityContext`, host extension via subinterfaces (`IToolCallSecurityContext`, planned `IRequestSecurityContext<T>`), claim conventions |
| `docs/core-concepts/policies.md` | `IAuthorizationPolicy`, when to override `IsAuthorized` vs `IsAuthorizedAsync` vs `Evaluate`, lifetime/registration |
| `docs/core-concepts/authorize-attribute.md` | `[Authorize("PolicyName")]`, `[AuthorizationPolicy("PolicyName")]`, host-side dispatch contract |
| `docs/core-concepts/sync-vs-async.md` | When to use which method, the `InvalidOperationException` pattern for I/O-bound policies |
| `docs/core-concepts/failure-shape.md` | **NEW** — `AuthorizationFailure`, `UnitResult<AuthorizationFailure>`, when to emit a custom code, the `DefaultDenyCode` constant |

**Per file: write the content → `git add docs/core-concepts/<name>.md` → `git commit -m "docs: add core-concepts/<name>"`**

---

## Task 5.8-5.10: Author `docs/guides/*.md`

Three separate atomic tasks.

| File | Topic |
|---|---|
| `docs/guides/writing-a-policy.md` | Worked example: `AdminOnlyPolicy`. Sync, async, with `Evaluate` override. |
| `docs/guides/host-integration.md` | How to extend `ISecurityContext` for a host (mirror the existing README's `IToolCallSecurityContext` example) |
| `docs/guides/testing-policies.md` | Unit-testing pattern: stub `ISecurityContext`, assert `Evaluate` returns expected `UnitResult` |

**Per file: write the content → `git add docs/guides/<name>.md` → `git commit -m "docs: add guides/<name>"`**

---

## Task 5.11: Author `docs/attributes.md`

**Files:**
- Create: `docs/attributes.md`

Content: full reference for `[Authorize]` and `[AuthorizationPolicy]` — constructor signatures, valid targets, behavior. Match the format of `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Resilience/docs/attributes.md`.

**Commit:** `git commit -m "docs: add attributes reference"`

---

## Task 5.12: Author `docs/performance.md`

**Files:**
- Create: `docs/performance.md`

Content: BenchmarkDotNet table (from Task 2.9) with "Allocated 0 B" + commentary. Reference the AOT smoke test as the second-line gate.

**Commit:** `git commit -m "docs: add performance benchmarks"`

---

## Task 5.13: Push the implementation branch + open PR

**Files:** none — git plumbing.

**Step 1: Push**

Run: `git push -u origin feat/v1-readiness`

**Step 2: Create PR**

```bash
gh pr create --title "feat: ZeroAlloc.Authorization 1.0 readiness" --body "$(cat <<'EOF'
## Summary
Implements the 1.0 readiness design at [docs/plans/2026-05-01-v1-readiness-design.md](docs/plans/2026-05-01-v1-readiness-design.md).

- `AuthorizationFailure` + `Evaluate`/`EvaluateAsync` returning `UnitResult<AuthorizationFailure>` (additive via DIM)
- `IsAotCompatible=true`, `samples/...AotSmoke`, `benchmarks/...Benchmarks`, AOT badge
- `PublicApiAnalyzers` + `PublicAPI.Shipped.txt`, ApiCompat CI step
- Pipeline alignment with `ZeroAlloc.Resilience` (`trigger-website.yml`, `Directory.Build.props` parity)
- Authored docs/ content for the docs site

## Test plan
- [ ] `dotnet test` passes
- [ ] `dotnet publish samples/ZeroAlloc.Authorization.AotSmoke -c Release -r linux-x64` succeeds with 0 IL2026/IL3050
- [ ] BenchmarkDotNet run shows 0 B for all four hot-path methods
- [ ] CI green: `lint-commits`, `build`, `aot-smoke`, `api-compat`

## Follow-ups (separate PRs / repos)
- `ZeroAlloc.Website`: scaffold `apps/docs-authorization`, register submodule, update README
- `ZeroAlloc-Net/.github`: add Authorization to the org profile packages table
- Cloudflare dashboard: create `za-docs-authorization` Worker service
- Repo settings: add `WEBSITE_DISPATCH_TOKEN` secret for `trigger-website.yml`

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Task 5.14: Scaffold `apps/docs-authorization` in `ZeroAlloc.Website` repo

**Files (in a different repo):**
- Run from: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Website`
- Reference donor: `apps/docs-notify`

**Step 1: Branch off main in the website repo**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Website
git fetch origin main
git switch -c feat/add-authorization-docs origin/main
```

**Step 2: Copy the lean template files**

```bash
mkdir -p apps/docs-authorization/src/css
cp apps/docs-notify/sidebars.ts apps/docs-authorization/sidebars.ts
cp apps/docs-notify/tsconfig.json apps/docs-authorization/tsconfig.json
cp apps/docs-notify/src/css/custom.css apps/docs-authorization/src/css/custom.css
```

**Step 3: Author the four unique files**

Create `apps/docs-authorization/package.json`, `apps/docs-authorization/docusaurus.config.ts`, `apps/docs-authorization/wrangler.jsonc`, `apps/docs-authorization/README.md` — same shape as the docs-cache files we wrote earlier in this conversation. Substitutions: `notify` → `authorization`, url `https://authorization.zeroalloc.net`, wrangler service name `za-docs-authorization`.

**Step 4: Commit**

```bash
git add apps/docs-authorization
git commit -m "feat: scaffold docs-authorization Docusaurus app"
```

---

## Task 5.15: Wire the submodule

**Files:**
- Modify: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Website/.gitmodules`

**Step 1: Add the submodule**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Website
git submodule add https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization repos/authorization
```

**Step 2: Verify**

Run: `git submodule status | grep authorization`

Expected: one line for `repos/authorization`.

**Step 3: Commit**

```bash
git add .gitmodules repos/authorization
git commit -m "feat: register ZeroAlloc.Authorization submodule"
```

---

## Task 5.16: Update website README

**Files:**
- Modify: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Website/README.md`

**Step 1: Add row to apps table**

```
| `apps/docs-authorization` | authorization.zeroalloc.net |
```

**Step 2: Add row to dev-commands list**

```
pnpm dev --filter @zeroalloc/docs-authorization   # authorization docs only
```

**Step 3: Add row to Cloudflare deploy table**

```
| `za-docs-authorization` | `pnpm build --filter @zeroalloc/docs-authorization` | `cd apps/docs-authorization && npx wrangler deploy` |
```

**Step 4: Commit + push + open PR**

```bash
git add README.md
git commit -m "docs: add docs-authorization to apps/dev/deploy tables"
git push -u origin feat/add-authorization-docs
gh pr create --title "feat: add docs-authorization site" --body "<body matching the recent docs-cache PRs>"
```

---

## Task 5.17: Update `.github` org profile README

**Files (in a different repo):**
- Modify: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.github/profile/README.md`

**Step 1: Branch off main in the .github repo**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.github
git fetch origin main
git switch -c docs/add-authorization origin/main
```

**Step 2: Add row to Packages table** + **add a per-package section**

Match the format of the existing 22 entries. The row + section text:

> `| [ZeroAlloc.Authorization](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization) | Authorization primitives — `[Authorize]` attribute + `IAuthorizationPolicy` contract + `ISecurityContext` for hosts to extend. Source-of-truth contract package consumed by AI.Sentinel and the planned `ZeroAlloc.Mediator.Authorization` | [![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Authorization.svg?style=flat-square)](https://www.nuget.org/packages/ZeroAlloc.Authorization) |`

**Step 3: Commit + push + open PR**

```bash
git add profile/README.md
git commit -m "docs: add ZeroAlloc.Authorization to packages table"
git push -u origin docs/add-authorization
gh pr create --title "docs: add ZeroAlloc.Authorization to org profile" --body "<short body>"
```

---

## Task 6: Cut the 1.0 release (post-merge)

**Pre-conditions:** the main implementation PR (Task 5.13) is merged on `main` of `ZeroAlloc.Authorization`.

### Task 6.1: Move `PublicAPI.Unshipped.txt` → `PublicAPI.Shipped.txt`

**Files:**
- Modify: `src/ZeroAlloc.Authorization/PublicAPI.Shipped.txt`
- Modify: `src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt`

**Step 1: Branch off main**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization
git fetch origin main && git switch -c chore/v1-public-api-baseline origin/main
```

**Step 2: Move entries**

Concatenate `PublicAPI.Unshipped.txt` into `PublicAPI.Shipped.txt`, then empty `PublicAPI.Unshipped.txt`.

```bash
cat src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt >> src/ZeroAlloc.Authorization/PublicAPI.Shipped.txt
> src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt
```

(On Windows shells use `type` and `set /p` equivalents.)

**Step 3: Sort `PublicAPI.Shipped.txt` alphabetically**

Run: `sort -u src/ZeroAlloc.Authorization/PublicAPI.Shipped.txt -o src/ZeroAlloc.Authorization/PublicAPI.Shipped.txt`

**Step 4: Verify clean build**

Run: `dotnet build -c Release`

Expected: 0 errors, 0 warnings.

**Step 5: Commit + PR**

```bash
git add src/ZeroAlloc.Authorization/PublicAPI.Shipped.txt src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt
git commit -m "chore: lock 1.0 public API surface"
git push -u origin chore/v1-public-api-baseline
gh pr create --title "chore: lock 1.0 public API surface" --body "Moves all entries from PublicAPI.Unshipped.txt to PublicAPI.Shipped.txt as the frozen 1.0 baseline."
```

### Task 6.2: Tag and release

**Step 1:** Once Task 6.1 is merged, the next release-please run will propose `v1.0.0`. Merge the release-please PR.

**Step 2: Verify on nuget.org**

After release-please publishes, verify:

- `https://www.nuget.org/packages/ZeroAlloc.Authorization/1.0.0` resolves
- `dotnet pack` output contains the AOT badge metadata
- A fresh consumer project can `dotnet add package ZeroAlloc.Authorization` and reference `IAuthorizationPolicy.EvaluateAsync`

**Step 3: Verify docs site**

After the trigger-website workflow fires, verify `https://authorization.zeroalloc.net` resolves and serves the latest `docs/`.

---

## Definition of done

- All tasks completed; all PRs merged.
- `nuget.org` shows `ZeroAlloc.Authorization 1.0.0`.
- CI on `main` is green: `lint-commits`, `build`, `aot-smoke`, `api-compat`.
- `authorization.zeroalloc.net` serves docs.
- Org profile lists the package.

## Notes for the executor

- This plan touches **three repos**: `ZeroAlloc.Authorization` (the bulk), `ZeroAlloc.Website` (Tasks 5.14-5.16), `ZeroAlloc-Net/.github` (Task 5.17). Each repo gets its own branch and PR.
- Two manual operations cannot be automated from inside this plan: (a) creating the `za-docs-authorization` Cloudflare Worker service via the dashboard, and (b) adding `WEBSITE_DISPATCH_TOKEN` to the Authorization repo's secrets. Surface these in the final summary.
- TDD applies to Workstream 1 (Tasks 1.2, 1.3). Workstreams 2-5 are mostly mechanical / authoring; the engineering gates (Workstream 3) and AOT verification (Tasks 2.4, 2.9) are the equivalent "test" steps for that work.
