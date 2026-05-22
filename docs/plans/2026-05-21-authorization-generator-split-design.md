# ZeroAlloc.Authorization 2.0.1 — Generator package split

Date: 2026-05-21
Status: Approved
Target release: ZeroAlloc.Authorization 2.0.1 + ZeroAlloc.Authorization.Generator 2.0.1

## Problem

ZeroAlloc.Authorization 2.0.0 bundles its Roslyn generator inside the main
NuGet at `analyzers/dotnet/cs/`. NuGet adds it as an `<Analyzer>` item in any
project that has a `PackageReference`. MSBuild then propagates `<Analyzer>`
items across `ProjectReference` edges — so a downstream project that has no
PackageReference at all still picks up the analyzer transitively. The
generator then runs in both upstream and downstream, emits the same
`AddZeroAllocAuthorization()` partial extension twice, and the consumer hits
CS0121 ambiguous-call at `Program.cs`.

The ZeroAlloc.Templates `za-clean` template hit this concretely: the
analyzer leaks from `MyApp.Application` → `MyApp.Api` and
`MyApp.Infrastructure` via `ProjectReference`. The template currently works
around it with a per-csproj MSBuild `<Target>` that strips the analyzer from
the `@(Analyzer)` item set before `CoreCompile`. The workaround is correct
but pushes packaging plumbing into every consumer.

`PrivateAssets="analyzers"` on the consumer's `PackageReference` does not
help — NuGet's `PrivateAssets` only filters PackageReference transitivity,
not MSBuild's `@(Analyzer)` flow across `ProjectReference`.

## Goals

- Remove the need for the template's MSBuild `<Target>` workaround.
- Strictly additive in 2.0.x — no breaking change for consumers already on
  2.0.0. (User preference: minimize major bumps; reach for additive
  deprecation first.)
- Match the conventions already proven in ZeroAlloc.Mediator +
  ZeroAlloc.Mediator.Generator so the template uses one consistent mental
  model across all ZeroAlloc generators.

## Design

### Architecture

Ship two packages from this repo. Both at version 2.0.1, lockstepped.

1. `ZeroAlloc.Authorization` 2.0.1 — unchanged runtime surface (attributes,
   `IAuthorizationPolicy`, `AuthorizerFor<T>`, `AuthorizationFailure`, DI
   extension). Continues to bundle the generator at `analyzers/dotnet/cs/`
   inside this package for backward compatibility with 2.0.0 consumers.

2. `ZeroAlloc.Authorization.Generator` 2.0.1 — NEW. Packages the Roslyn
   generator only. `developmentDependency=true` in nuspec so the package
   does not flow transitively even if a consumer omits `PrivateAssets="all"`.

### buildTransitive guard

Inside `ZeroAlloc.Authorization` ship `buildTransitive/ZeroAlloc.Authorization.targets`:

```xml
<Project>
  <Target Name="ZeroAllocAuthorization_RemoveBundledAnalyzerWhenStandalonePresent"
          BeforeTargets="CoreCompile">
    <ItemGroup Condition="'@(Analyzer->WithMetadataValue('NuGetPackageId','ZeroAlloc.Authorization.Generator')->Count())' != '0'">
      <Analyzer Remove="@(Analyzer->WithMetadataValue('NuGetPackageId','ZeroAlloc.Authorization'))" />
    </ItemGroup>
  </Target>
</Project>
```

NuGet auto-imports this targets file into every project consuming the
package (direct and transitive). The target removes the bundled analyzer
only when the standalone Generator package is present, preventing
double-generation for consumers who follow the new pattern.

The `@(Analyzer->WithMetadataValue(...))` item-function form is mandatory.
`Condition="'%(Analyzer.NuGetPackageId)' == '...'"` does not survive the
SDK's analyzer item flow reliably. The template hit this same constraint
when building its workaround.

Also ship an empty `build/ZeroAlloc.Authorization.targets` stub to silence
SDK warnings emitted by some older NuGet/SDK combos when a package has
`buildTransitive/` without a matching `build/` file.

### Consumer-side patterns

Idiomatic 2.0.1 consumer (matches the Mediator pattern):

```xml
<!-- Application project (declares [Policy]) -->
<PackageReference Include="ZeroAlloc.Authorization" />
<PackageReference Include="ZeroAlloc.Authorization.Generator" PrivateAssets="all" />

<!-- Api / Infrastructure project (does not declare [Policy]) -->
<!-- Nothing — analyzer no longer leaks via ProjectReference because the
     Generator package was consumed with PrivateAssets="all" in Application. -->
```

Backward-compat 2.0.0-style consumer (still works):

```xml
<PackageReference Include="ZeroAlloc.Authorization" />
<!-- Bundled analyzer runs; downstream consumers need the template-style
     MSBuild workaround OR can upgrade to the split package layout. -->
```

### Behavior matrix

| Consumer setup                                     | Bundled analyzer | Standalone Generator | After buildTransitive guard |
|----------------------------------------------------|------------------|----------------------|-----------------------------|
| Direct ref to ZA.Authorization only (2.0.0 style)  | yes              | no                   | bundled kept → generator runs, no break |
| Direct ref + new Generator package (2.0.1 style)   | yes              | yes                  | bundled removed → standalone wins |
| Transitive consumer (no PackageReference)          | flows via MSBuild| flows only if upstream has it | matches upstream's setup; guard runs locally too |

## Release-please configuration

`release-please-config.json` — add the new Generator component, one-shot
`release-as` to land at lockstepped 2.0.1:

```json
{
  "packages": {
    "src/ZeroAlloc.Authorization": {
      "package-name": "ZeroAlloc.Authorization",
      "component": "authorization",
      "include-component-in-tag": true,
      "release-as": "2.0.1"
    },
    "src/ZeroAlloc.Authorization.Generator": {
      "package-name": "ZeroAlloc.Authorization.Generator",
      "component": "authorization-generator",
      "include-component-in-tag": true,
      "initial-version": "2.0.0",
      "release-as": "2.0.1"
    }
  }
}
```

`.release-please-manifest.json`:

```json
{
  ".": "2.0.1",
  "src/ZeroAlloc.Authorization": "2.0.1",
  "src/ZeroAlloc.Authorization.Generator": "2.0.0"
}
```

Tag scheme uses the existing `include-component-in-tag` pattern. First
release tags are `authorization-v2.0.1` and `authorization-generator-v2.0.1`.

After the first publish verifies, a follow-up PR removes the one-shot
`release-as` overrides so conventional-commit path-routing drives all
subsequent bumps. Same hygiene path the ZeroAlloc.Mediator repo took after
its 2.0.0 publish.

## Testing

- Existing `ZeroAlloc.Authorization.Tests` and `ZeroAlloc.Authorization.Generator.Tests`
  pass unchanged. The runtime contract and generator snapshot contract are
  both unchanged.
- AOT smoke (`samples/ZeroAlloc.Authorization.AotSmoke`) continues to pass
  unchanged. No `[DynamicDependency]` annotations need touching.
- NEW pack-integrity smoke test (`tests/ZeroAlloc.Authorization.PackSmoke/`,
  CI-only script). Builds + packs both packages into a temp local feed,
  scaffolds three throwaway projects mirroring the template shape, asserts:
  - `TestApp.Application` and `TestApp.Api` both compile with no CS0121.
  - No `<Analyzer>` items referencing `ZeroAlloc.Authorization.Generator.dll`
    appear in `TestApp.Api`'s build (verified via `dotnet msbuild
    -getItem:Analyzer` or by reading `dotnet build -bl` output).
  - Backward-compat scenario (Application uses only the bundled analyzer)
    still produces the expected `obj/Generated/...g.cs` output.
- NEW buildTransitive guard unit test — smaller, faster than the pack test.
  Synthesises a project with both `@(Analyzer)` items, runs the guard
  target, asserts the bundled one is removed.

Manual verification before tagging 2.0.1:

- Pack locally, install in the template repo on a scratch branch, confirm
  the template builds without the MSBuild workaround Target.
- Confirm `authorization-v2.0.1` and `authorization-generator-v2.0.1`
  GitHub releases both exist after the Release Please PR merges and the
  publish workflow runs.

## Out of scope

- Removing the bundled analyzer from the main `ZeroAlloc.Authorization`
  package. That is a breaking change for any 2.0.0 consumer that relies on
  the bundled-only pattern → deferred to a future 3.0 cleanup.
- Any change to runtime types or generator output shape. This is a
  packaging-only release.
- Template repo changes — sequenced as a separate PR after 2.0.1 publishes.

## Sequenced follow-ups

1. (after 2.0.1 publishes) Template PR
   `chore/template-drop-authorization-analyzer-workaround`: bump versions,
   add Generator PackageReference to `MyApp.Application`, delete the
   `<Target>` workaround from `MyApp.Api.csproj` and
   `MyApp.Infrastructure.csproj`.
2. (after template PR merges) Small release-please cleanup PR removing the
   one-shot `release-as` overrides from this repo's config.
