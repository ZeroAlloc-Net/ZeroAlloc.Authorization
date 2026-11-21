# AGENTS.md: ZeroAlloc.Authorization

Source-generated authorization policies for any .NET host. Two NuGet packages ship from `src/`: `ZeroAlloc.Authorization` (the attributes, `ISecurityContext`, `IAuthorizationPolicy`, `AuthorizerFor<T>`; it also bundles the generator) and `ZeroAlloc.Authorization.Generator` (the Roslyn generator alone, for the split-package setup). Consumers are ASP.NET hosts, mediators and agent tool-call dispatchers. The library targets `net10.0` and `net11.0`; the generator targets `netstandard2.0`.

## Layout
- `src/ZeroAlloc.Authorization.Generator` emits the DI registration and one `AuthorizerFor<T>` dispatcher per request type. Its `ZAUTH001` to `ZAUTH009` diagnostics are tracked in `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md`.
- `src/ZeroAlloc.Authorization/build` and `buildTransitive` hold MSBuild targets that ship in the packages. They filter the bundled and the standalone generator by consumer properties; the same DLL name exists in both packages, so the target order matters (see the comments in the targets before changing them).
- `tests/ZeroAlloc.Authorization.Generator.Tests/Snapshots` holds Verify snapshots. They are data: the project removes `*.verified.*` from compilation.
- `samples/ZeroAlloc.Authorization.AotSmoke` is a NativeAOT sample, not a shipped package. `benchmarks/` is not shipped either.
- `apicompat-suppressions.xml` lists intentional breaking changes against the released baseline, used by the shared `api-compat` workflow.

## Build and test
Prerequisite: the .NET SDK pinned in `global.json` (11.0.100, roll forward `latestMinor`). Test projects target `net10.0` and `net11.0`, so both the .NET 10 and the .NET 11 runtime must be installed.

CI job `build` in `.github/workflows/ci.yml`, in this order:
- `dotnet restore ZeroAlloc.Authorization.slnx`
- `dotnet build ZeroAlloc.Authorization.slnx --configuration Release --no-restore -p:Version=$VERSION`
- `dotnet test ZeroAlloc.Authorization.slnx --configuration Release --no-build --verbosity normal --logger "trx;LogFilePrefix=test-results" --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults`
- `dotnet pack ZeroAlloc.Authorization.slnx --configuration Release --no-build --output ./artifacts -p:Version=$VERSION`

CI job `aot-smoke` in `ci.yml` (needs clang and zlib1g-dev):
- `dotnet publish samples/ZeroAlloc.Authorization.AotSmoke/ZeroAlloc.Authorization.AotSmoke.csproj -r linux-x64 -c Release -o ./aot-out`
- `./aot-out/ZeroAlloc.Authorization.AotSmoke`

`$VERSION` in CI is the GitVersion SemVer (`dotnet dotnet-gitversion /output json /showvariable SemVer`). Locally any version works, for example `-p:Version=1.0.0-local`.

Inner loop (verified locally, no CI job runs these as written):
- `dotnet build ZeroAlloc.Authorization.slnx -c Release -p:Version=1.0.0-local`
- `dotnet test tests/ZeroAlloc.Authorization.Tests -c Release --no-build -f net10.0 --filter "FullyQualifiedName~AllocationBudgetTests"`

## Conventions and gotchas
- Pack needs a version. `Directory.Build.props` fails pack with `ZAVERSION001` when no `-p:Version` was given and with `ZAVERSION002` when the built assemblies carry another version. With `--no-build`, pass the same `-p:Version` to the build step.
- Warnings are errors, and the public API is tracked: adding or changing a public member without a `PublicAPI.Unshipped.txt` entry breaks the build (`RS0016`, `RS0017`). Do the same for analyzer rules in `AnalyzerReleases.Unshipped.md`. Do not move entries to the `Shipped` files by hand: the release PR does that, and the `release-tracking` CI job checks it.
- `AllocationBudgetTests` assert 0 bytes per check on the allow and deny paths. A change in the evaluation path that allocates fails them; fix the path, do not raise a budget.
- Do not reference the generator by a hard-coded `bin` path. Projects that need it as an analyzer use a `ProjectReference` with `OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"`, as the test and sample projects do.
- A changed generator output means updating the Verify snapshots in `Snapshots/`. Review every `*.received.*` diff before accepting it as `*.verified.*`.
- `PackSmokeTests` packs the real `src` projects into a temp feed with its own artifacts path. Keep that path: packing into the repo's `bin` and `obj` once shipped a 9.9.9-stamped assembly.
- The `AotSmoke` sample treats `IL2026`, `IL2067`, `IL2075`, `IL2091`, `IL3050` and `IL3051` as errors. Any reflection in generated code shows up there.

<!-- org-core:start sha256=8faf417fa86f11bd8ad9bc98dfa4683fba44e440291e84c6cb9b0ec3ca0c0e7e (generated from docker-nuc scripts/agents-md/org-core-zeroalloc.md; do not edit here) -->
## Org rules

These rules hold in every ZeroAlloc-Net repo. Each library makes two promises: it works under
NativeAOT and trimming, and its hot path allocates nothing. The 2026-10-11 sweep found 16 of 28 repos
allocating on the hot path, most with docs claiming 0 B.

### Hot path

The operation a consumer runs repeatedly: `Send`, a cache hit, `Validate`, `Map`, `Serialize`, a
resilience call, a state transition, an append, a worker poll, including the idle poll. Startup and
registration are not hot path. For a source generator, the hot path is the code it emits: judge the
snapshots and the generated output.

### Allocation traps the sweep found

| Pattern | Instead |
|---|---|
| `async ValueTask` that only awaits and returns | `return new ValueTask<T>(inner.XAsync(ct));` when the method only forwards a `Task<T>` (return a forwarded `ValueTask<T>` directly). The wrapper stores a synchronous throw in the returned task; the direct return lets it, and the cancellation status of the callee, reach the caller. Keep the `async` wrapper when callers depend on either |
| `Task.WhenAll` over tasks that already completed | skip the await only when every task has `IsCompletedSuccessfully` (`Status == TaskStatus.RanToCompletion` on `netstandard2.0`); otherwise `await Task.WhenAll`, which also reports faults of completed tasks |
| `TagList` or log `params object[]` built before `Enabled`/`IsEnabled` | check first, or `LoggerMessage.Define` |
| event args allocated before checking for subscribers | copy the handler to a local, check, then allocate |
| `new Serializer()` / `new Utf8JsonWriter()` per call | cache stateless instances, reuse writers with `Reset` |
| `GetString()` / `ToString()` round-trips in JSON converters | parse and format through spans |
| `ToArray()` / `ToList()` copies between layers | pass `ReadOnlyMemory<T>` or spans, copy once at most |
| LINQ in emitted code | emit `for` loops |
| `yield` iterator behind a public `GetEnumerator` | public struct enumerator, from the first release |
| `Enum.IsDefined(typeof(E), v)` with a boxed value, `decimal.GetBits(v)` to read the scale | `Enum.IsDefined<E>(v)` when `v` has type `E` (.NET 5 or later, so not on .NET Framework, `netstandard2.0`, `netstandard2.1` or .NET Core 3.1); `v.Scale` for a scale read only (.NET 7 or later, never for the coefficient or sign). Keep the old call on older targets |
| configuration parsed or read per call | parse once and cache |
| an `AsyncLocal` write per call | pass state explicitly |
| a capturing lambda or method-group delegate per call | cache the delegate in a field, or a static lambda |
| a per-key object that is never removed | remove on release; otherwise it is a leak |

Inherent allocations are fine when documented: the consumer's own result, an exception on a failure
path, a one-time cache fill.

### Tests and benchmarks

- Every hot path has an allocation gate test (`AllocationGate` from ZeroAlloc.TestHelpers) that measures
  the whole public call, including any state setup.
- Budgets are exact. If a path allocates, assert that number and comment what it is.
- Test the generated code, not a hand-written stand-in, and cover every shape: each attribute, nested
  collections, and the asynchronous case with a fake that returns pending tasks.
- Benchmarks run in CI through the shared `benchmark-smoke.yml` and target the library's LTS
  frameworks. The benchmark body allocates nothing besides the library itself. Run locally with
  `--inProcess`.
- Publish exact allocations and same-run ratios against a `Baseline`, never absolute timings. Run with
  `--launchCount 3`, pin CPUs with `taskset`, give the spread, and label every table.

### NativeAOT and trimming

- Packable runtime projects, and projects consumers publish, set `IsAotCompatible=true` on modern
  target frameworks. Analyzers and source generators target `netstandard2.0`.
- No reflection or runtime code generation on a supported path. An opt-in reflection fallback carries
  honest `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`. Every suppression must be true;
  remove dead ones.
- The AOT smoke sample publishes with `TreatWarningsAsErrors=true`. If a third-party warning really
  blocks it, use a narrow, justified suppression for that warning. `WarningsNotAsErrors` selects codes,
  not assemblies, so it would also downgrade the same warning from generated code.
- The smoke runs the hot path of every package in the repo. A package without a smoke has unknown AOT
  status. Adapters run against real services in CI service containers; publish-only is a last resort.
  App and template smokes make authenticated requests.

### Docs and shipping

- Docs match measurements. After a hot-path change, re-measure and update `docs/performance.md` and the
  README. Never write "0 B" without a test that asserts it.
- New public API is `feat:`, even for a performance change. Public API changes are additive: never add
  an optional parameter to a shipped signature. A breaking end state goes in a `next-major` issue.
- A behaviour change gets a `## Behaviour change` section in the PR body.
- Conventional Commits with release-please. Subjects start lowercase, body lines stay within 100
  characters, and a body never contains nested parentheses: release-please drops the whole commit.
- Local packs use `9.9.9-local.<sha>`. After a release, verify the package on NuGet.
- Fix review findings in the PR; file an issue only for what cannot go in it.

Full rules, template wiring lessons and the reasons behind each rule: https://github.com/ZeroAlloc-Net/.github/blob/main/AGENTS.md
<!-- org-core:end -->
