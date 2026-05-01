# ZeroAlloc.Authorization 1.0 readiness — design

**Status:** approved 2026-05-01. Implementation plan to follow.

## Goal

Ship `ZeroAlloc.Authorization 1.0` with a contract surface that is (a) committed-to under SemVer, (b) certified AOT and zero-allocation, and (c) integrated into the rest of the ecosystem (CI shape, docs site, org profile). Once 1.0 lands, companion hosts (`AI.Sentinel`, `ZeroAlloc.Mediator.Authorization`) consume it as a `<PackageReference>` floor and evolve independently per the [versioning contract](../backlog.md#versioning-contract).

## Scope (in)

| # | Item | Source |
|---|---|---|
| 1 | `AuthorizationFailure` + `Evaluate`/`EvaluateAsync` returning `UnitResult<AuthorizationFailure>` | Backlog item #4 |
| 2 | AOT/zero-alloc certification: `IsAotCompatible=true`, AOT smoke sample, BenchmarkDotNet project, AOT badge | Backlog item #6 |
| 3 | Engineering gates: `PublicApiAnalyzers` + `PublicAPI.Shipped.txt`, ApiCompat CI step | Versioning contract |
| 4 | Pipeline alignment with the rest of the family (trigger-website.yml, samples/benchmarks projects in `.slnx`, Directory.Build.props parity) | This design |
| 5 | Docs site integration: author content under `docs/`, scaffold `apps/docs-authorization` in the website, register the submodule, update the website README, add to `.github` org profile | This design |

## Scope (out)

- Backlog items #1, #2, #3, #5 — additive, can land in 1.x without breaking the contract. Defer until concrete host need.
- Multi-tenancy primitives — see [decisions](../backlog.md#decisions-multi-tenancy).
- A 1.0 release of any companion (Mediator.Authorization, etc.) — those track this 1.0 as a floor and ship on their own cadence.

---

## Section 1 — `AuthorizationFailure` + `Evaluate`/`EvaluateAsync`

### Failure type

```csharp
public readonly struct AuthorizationFailure
{
    public const string DefaultDenyCode = "policy.deny";

    public string Code { get; }
    public string? Reason { get; }

    public AuthorizationFailure(string code, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        Code = code;
        Reason = reason;
    }
}
```

- `readonly struct` keeps zero-alloc on the hot path.
- `Code` is non-null/non-empty machine-readable; e.g. `"policy.deny.role"`, `"tenant.inactive"`.
- `Reason` is optional human-readable text — hosts may surface in API responses or logs; treat as untrusted-for-display unless the policy author guarantees it.
- `DefaultDenyCode = "policy.deny"` is the constant used when wrapping a `false` from `IsAuthorized` without a more specific code. Hosts can match on it to detect "policy denied without explanation" vs an explicitly-coded deny.

### Contract evolution — additive via DIM

```csharp
using ZeroAlloc.Results;

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
        => IsAuthorized(ctx)
            ? UnitResult.Success<AuthorizationFailure>()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode);

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

### Migration

- Existing implementations of `IsAuthorized` and `IsAuthorizedAsync` keep working unchanged. DIM handles them.
- New policies that want richer deny info override `Evaluate`/`EvaluateAsync` instead.
- Hosts call `EvaluateAsync` and get either a `bool`-derived result or a structured one — single API path.

### New dependency

```xml
<PackageReference Include="ZeroAlloc.Results" Version="0.1.4" />
```

First cross-package dependency for `ZeroAlloc.Authorization`. Pin the floor to the version that has `UnitResult<E>` plus the implicit `E → UnitResult<E>` conversion. Renovate handles future bumps.

---

## Section 2 — AOT certification + benchmarks

### csproj change

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <RootNamespace>ZeroAlloc.Authorization</RootNamespace>
    <PackageId>ZeroAlloc.Authorization</PackageId>
    <IsAotCompatible>true</IsAotCompatible>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ZeroAlloc.Results" Version="0.1.4" />
  </ItemGroup>
</Project>
```

`IsAotCompatible` enables trim/AOT analyzers in-build; the compiler fires `IL2026`/`IL3050` if any future code introduces reflection or dynamic-code patterns. This is the actual gate; the CI smoke test on top is belt-and-braces.

### `samples/ZeroAlloc.Authorization.AotSmoke/`

A console app that exercises every public path. Pattern copied from `ZeroAlloc.Resilience.AotSmoke`. Csproj sets `<PublishAot>true</PublishAot>`, `<TargetFramework>net10.0</TargetFramework>`, references the main library via `<ProjectReference>`. If the main library introduces a reflection-only path, ILC fails the publish.

`Program.cs` sketch:

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

### `benchmarks/ZeroAlloc.Authorization.Benchmarks/`

BenchmarkDotNet harness with `[MemoryDiagnoser]` asserting 0 B for the four hot-path methods (`IsAuthorized`, `IsAuthorizedAsync`, `Evaluate`, `EvaluateAsync` — happy path).

Runs locally, not in CI (BenchmarkDotNet is slow and noisy on shared runners; gating happens via the analyzer + AOT smoke test instead). The benchmark project's purpose is to surface allocation regressions during local dev and to populate the README's perf table.

### CI job — `aot-smoke`

New job in `.github/workflows/ci.yml`. Copy verbatim from `ZeroAlloc.Resilience/.github/workflows/ci.yml`'s `aot-smoke` job, swap project names. Runs on `ubuntu-latest`, installs `clang` + `zlib1g-dev`, publishes the sample with `-r linux-x64`, executes the resulting binary, asserts exit code 0.

### README

Add the AOT badge alongside the existing badges:

```markdown
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
```

Plus a "Benchmarks" section populated once benchmarks first run.

### Open assumption

The smoke test only runs on `linux-x64`. Other ZeroAlloc libraries do the same; cross-platform AOT validation is a 1.x add if it ever proves necessary.

---

## Section 3 — Engineering gates

### Two complementary tools

| Tool | Gates | Runs at | Failure mode |
|---|---|---|---|
| `Microsoft.CodeAnalysis.PublicApiAnalyzers` | Every public-surface change visible at compile time | Every build (local + CI) | Compile error (`RS0016`/`RS0017`) |
| `Microsoft.DotNet.ApiCompat.Tool` | Binary/source compat between the new build and the previously published version on nuget.org | Pre-release CI step | CI step fails with a list of breaking changes |

### `PublicApiAnalyzers` wiring

In `src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj`:

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

In `Directory.Build.props` (or local csproj):

```xml
<PropertyGroup>
  <WarningsAsErrors>$(WarningsAsErrors);RS0016;RS0017</WarningsAsErrors>
</PropertyGroup>
```

### Initial baseline

Generated once via the analyzer's IDE code-fix ("Add to public API"); populates `PublicAPI.Unshipped.txt`. At 1.0 release time, move all entries to `PublicAPI.Shipped.txt`. From then on, every new public symbol must be recorded in Unshipped within the same PR; Shipped is rotated only at release time.

### `api-compat` CI job

New step in `.github/workflows/ci.yml`. Compares the current build's surface against the latest published `1.x` version. No-ops until 1.0 is on nuget.org (first 1.0 has no baseline to compare against).

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

### Combined guarantees

- Every public-surface change requires explicit acknowledgement in `PublicAPI.Unshipped.txt` in the same PR.
- A PR that removes a public symbol fails to compile (`RS0017`).
- A PR that adds a public symbol without recording it fails to compile (`RS0016`).
- A 1.x release that accidentally breaks 1.0's binary contract fails CI before publishing.
- Major-version bumps follow the same gates, with an updated `PublicAPI.Shipped.txt` baseline.

---

## Section 4 — Pipeline alignment

**Donor:** [`ZeroAlloc.Resilience`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience). Same shape as the 1.0 target (contract-style + AOT-certified + benchmarks).

| File | Current state in Authorization | Action |
|---|---|---|
| `.github/workflows/ci.yml` | `lint-commits` + `build` only | Add `aot-smoke` job (Section 2) and `api-compat` job (Section 3) |
| `.github/workflows/release-please.yml` | Exists | Verify version-bump and publish step; cross-check with Resilience for any release-time api-compat gate |
| `.github/workflows/publish.yml` | Authorization-specific, exists | Audit for redundancy with release-please; delete if duplicate, document if distinct |
| `.github/workflows/trigger-website.yml` | **Missing** | Copy verbatim from Resilience. Dispatches `submodule-update` to `ZeroAlloc-Net/.website` whenever `docs/**` changes on `main`. Requires `WEBSITE_DISPATCH_TOKEN` secret on the repo (fine-grained PAT with `Actions: write` on `ZeroAlloc-Net/.website`). |
| `Directory.Build.props` | Exists | Cross-check vs Resilience's: `LangVersion=latest`, `Nullable=enable`, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`. Adjust to match. |
| `.slnx` | Has src + tests | Add `samples/ZeroAlloc.Authorization.AotSmoke` and `benchmarks/ZeroAlloc.Authorization.Benchmarks` once those projects exist. |
| `renovate.json` | Verify exists | Copy from Resilience if missing. Same schedule + auto-merge rules as the rest of the family. |

---

## Section 5 — Docs site integration

### 5.1 Author docs content under this repo's `docs/`

The website pulls from `repos/authorization/docs/` (once the submodule is wired). Today this repo's `docs/` only contains `backlog.md` (internal) and `plans/2026-05-01-v1-readiness-design.md` (this design, internal). Add user-facing content:

```
docs/
├── backlog.md                              (internal — site excludes via 'docs-authorization' app config)
├── plans/
│   └── 2026-05-01-v1-readiness-design.md   (internal — site excludes 'plans/**')
├── index.md                                (landing page)
├── getting-started.md
├── core-concepts/
│   ├── security-context.md
│   ├── policies.md
│   ├── authorize-attribute.md
│   ├── sync-vs-async.md
│   └── failure-shape.md                    (new — covers AuthorizationFailure + Evaluate)
├── guides/
│   ├── writing-a-policy.md
│   ├── host-integration.md                 (extending ISecurityContext via subinterfaces)
│   └── testing-policies.md
├── attributes.md
└── performance.md                          (BenchmarkDotNet table from Section 2)
```

The `docs-authorization` Docusaurus app excludes `**/backlog.md`, `**/plans/**`, `**/README.md`, `**/pre-push-review*.md` from its sidebar — same `exclude` list as sibling apps.

### 5.2 Scaffold `apps/docs-authorization/` in `ZeroAlloc.Website`

Same pattern used for the five most recent docs apps (`docs-cache`, `docs-outbox`, `docs-resilience`, `docs-saga`, `docs-statemachine`). Copy `apps/docs-notify` as the lean template.

Files:
- `package.json` — name `@zeroalloc/docs-authorization`
- `docusaurus.config.ts` — title `ZeroAlloc.Authorization`, url `https://authorization.zeroalloc.net`, `path: '../../repos/authorization/docs'`, `staticDirectories: ['static', '../../repos/authorization/assets']` (Authorization repo has `assets/`, confirm `icon.png` present; otherwise use `sharedFavicon` per the docs-saga pattern)
- `sidebars.ts` (autogenerated)
- `tsconfig.json`
- `wrangler.jsonc` — `name: za-docs-authorization`, `compatibility_date: 2026-05-01`
- `src/css/custom.css` — verbatim copy from `docs-notify`
- `README.md`

### 5.3 Wire the submodule

```bash
cd /path/to/ZeroAlloc.Website
git submodule add https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization repos/authorization
```

Updates `.gitmodules` and stages the gitlink.

### 5.4 Update `ZeroAlloc.Website/README.md`

Three rows added:
- Apps table: `apps/docs-authorization | authorization.zeroalloc.net`
- Dev commands: `pnpm dev --filter @zeroalloc/docs-authorization`
- Cloudflare deploy table: `za-docs-authorization | pnpm build --filter @zeroalloc/docs-authorization | cd apps/docs-authorization && npx wrangler deploy`

### 5.5 Cloudflare Worker service

Manual dashboard action (cannot be automated from here): create `za-docs-authorization` with:
- Build command: `pnpm build --filter @zeroalloc/docs-authorization`
- Deploy command: `cd apps/docs-authorization && npx wrangler deploy`
- `NODE_VERSION=20`, root `/`

### 5.6 Update `.github` org profile README

Add to `ZeroAlloc-Net/.github/profile/README.md`:
- Row in the Packages table: `[ZeroAlloc.Authorization](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization) | Authorization primitives — `[Authorize]` attributes, `IAuthorizationPolicy` contract, `ISecurityContext` for hosts to extend. Source-of-truth contract package consumed by AI.Sentinel and `ZeroAlloc.Mediator.Authorization`. | NuGet badge`
- A per-package section matching the existing format, with description + a short code sample (one policy, one `[Authorize]`).

---

## Risks & mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| New `ZeroAlloc.Results` dep introduces version coupling | Medium | Pin a low floor (0.1.4); rely on Renovate; document in versioning contract |
| `EvaluateAsync` default-impl forces `async` keyword on the interface — older C# / lang-versions may not support DIM async | Low | Project targets net8+, C# 12 — DIM async is fully supported. Verify `LangVersion` covers it. |
| AOT smoke test on `linux-x64` only misses Windows-specific reflection regressions | Low | Acceptable — matches family pattern. Cross-platform AOT smoke is a 1.x add if it proves necessary. |
| `apps/docs-authorization` Cloudflare service must be created in dashboard manually | Low | One-time action; documented in this design (5.5). |
| `WEBSITE_DISPATCH_TOKEN` secret required on the repo for `trigger-website.yml` | Low | Documented; provisioning happens once. |
| ApiCompat baseline doesn't exist for first 1.0 → step is a no-op on first run | None | By design — guarded with `if [ -z "$latest" ]`. Subsequent 1.x releases have a baseline. |

## Definition of done

- All sections 1-5 implemented; all CI jobs (`build`, `aot-smoke`, `api-compat`) green on `main`.
- `dotnet pack` produces `ZeroAlloc.Authorization.1.0.0.nupkg` containing the contract DLL with the new `AuthorizationFailure` and DIM evaluate methods.
- BenchmarkDotNet run shows 0 B allocated for `IsAuthorized`, `IsAuthorizedAsync`, `Evaluate`, `EvaluateAsync` happy paths.
- `authorization.zeroalloc.net` resolves and serves the docs from `repos/authorization/docs/`.
- The org profile README at `ZeroAlloc-Net/.github` lists Authorization in the Packages table.
- `PublicAPI.Shipped.txt` contains exactly the 1.0 surface; `PublicAPI.Unshipped.txt` is empty at release time.

## Next step

Invoke the `writing-plans` skill to break this design into an executable, ordered implementation plan with atomic tasks per section.
