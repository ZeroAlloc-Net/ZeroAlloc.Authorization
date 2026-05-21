# ZeroAlloc.Authorization 2.0.1 — Generator package split (implementation plan)

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship `ZeroAlloc.Authorization` 2.0.1 + a NEW `ZeroAlloc.Authorization.Generator` 2.0.1 package, with a `buildTransitive` guard that auto-removes the bundled analyzer when the standalone Generator package is present, so the ZA.Templates `za-clean` template can drop its per-csproj MSBuild `<Target>` workaround.

**Architecture:** Two NuGets out of this repo, both at 2.0.1, lockstepped. Main package keeps the bundled analyzer for backward-compat with 2.0.0 consumers, but ships a `buildTransitive/ZeroAlloc.Authorization.targets` file that removes the bundled analyzer when the standalone Generator package is also referenced. Standalone Generator package uses `developmentDependency=true` so it never flows transitively. Release-please stays on the single existing `.` component; one-shot `release-as: "2.0.1"` lands the patch version.

**Tech Stack:** .NET SDK 10, NuGet pack, MSBuild `buildTransitive` convention, Roslyn IIncrementalGenerator, release-please simple release-type, xUnit for the pack-integrity smoke test.

**Design doc:** `docs/plans/2026-05-21-authorization-generator-split-design.md` (note: Section 3 simplified at implementation time — single release-please component instead of two, see Task 5).

**Working branch:** `feat/generator-package-split-201` (already created off `main` at `b41033a`).

---

## Task 1: Make the Generator project packable

**Files:**
- Modify: `src/ZeroAlloc.Authorization.Generator/ZeroAlloc.Authorization.Generator.csproj`

**Step 1: Edit the csproj**

Replace the contents with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <!-- RS2008: Analyzer release tracking — not packaging as a stand-alone analyzer, descriptors live alongside generator. -->
    <NoWarn>$(NoWarn);RS2008</NoWarn>

    <!-- Pack as a generator-only package. -->
    <IsPackable>true</IsPackable>
    <PackageId>ZeroAlloc.Authorization.Generator</PackageId>
    <Description>Source generator for ZeroAlloc.Authorization — emits per-assembly DI registrations and AuthorizerFor&lt;T&gt; dispatchers from [Policy] and [RequirePolicy] declarations. Reference alongside ZeroAlloc.Authorization with PrivateAssets="all" to avoid analyzer flow across ProjectReference.</Description>
    <PackageTags>authorization;source-generator;analyzer;dotnet;zeroalloc</PackageTags>
    <!-- developmentDependency: never flows transitively even without PrivateAssets="all". -->
    <DevelopmentDependency>true</DevelopmentDependency>
    <!-- No runtime payload — ship the analyzer DLL only, not in lib/. -->
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <!-- Suppress the "your package has no lib/ or ref/ folder" warning. -->
    <NoWarn>$(NoWarn);NU5128</NoWarn>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.3.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="5.3.0" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <!-- Pack the generator DLL into analyzers/dotnet/cs/ — the standard
         Roslyn-component location NuGet auto-converts into @(Analyzer). -->
    <None Include="$(OutputPath)$(AssemblyName).dll"
          Pack="true"
          PackagePath="analyzers/dotnet/cs"
          Visible="false" />
  </ItemGroup>
</Project>
```

**Step 2: Pack and inspect**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization
dotnet pack src/ZeroAlloc.Authorization.Generator -c Release -p:PackageVersion=2.0.1-test -o /tmp/za-auth-pack-test
```

Expected: `/tmp/za-auth-pack-test/ZeroAlloc.Authorization.Generator.2.0.1-test.nupkg` created.

**Step 3: Verify package contents**

```bash
# .nupkg is a zip; list contents
unzip -l /tmp/za-auth-pack-test/ZeroAlloc.Authorization.Generator.2.0.1-test.nupkg
```

Expected:
- `analyzers/dotnet/cs/ZeroAlloc.Authorization.Generator.dll` present
- NO `lib/` folder (because `IncludeBuildOutput=false`)
- NO `Microsoft.CodeAnalysis.CSharp` listed as a dependency in the .nuspec (because `SuppressDependenciesWhenPacking=true`)

Verify the nuspec inside:

```bash
unzip -p /tmp/za-auth-pack-test/ZeroAlloc.Authorization.Generator.2.0.1-test.nupkg ZeroAlloc.Authorization.Generator.nuspec | grep -E "developmentDependency|<dependencies"
```

Expected: `<developmentDependency>true</developmentDependency>` line present; `<dependencies>` either absent or empty.

**Step 4: Commit**

```bash
git add src/ZeroAlloc.Authorization.Generator/ZeroAlloc.Authorization.Generator.csproj
git commit -m "feat(pack): make ZeroAlloc.Authorization.Generator a standalone NuGet package

Flips IsPackable=true, configures the project to emit only the
analyzer DLL at analyzers/dotnet/cs/ with developmentDependency=true.
Companion to the main ZeroAlloc.Authorization 2.0.x bundled analyzer;
consumers opting into this package with PrivateAssets=\"all\" avoid
the @(Analyzer) leak across ProjectReference edges."
```

---

## Task 2: Add `buildTransitive` guard to the main package

**Files:**
- Create: `src/ZeroAlloc.Authorization/buildTransitive/ZeroAlloc.Authorization.targets`
- Create: `src/ZeroAlloc.Authorization/build/ZeroAlloc.Authorization.targets`
- Modify: `src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj`

**Step 1: Write the buildTransitive guard**

Create `src/ZeroAlloc.Authorization/buildTransitive/ZeroAlloc.Authorization.targets`:

```xml
<Project>
  <!--
    Auto-imported by NuGet into every project that consumes
    ZeroAlloc.Authorization (direct or transitive).

    When the standalone ZeroAlloc.Authorization.Generator package is ALSO
    referenced, this target removes the analyzer bundled inside
    ZeroAlloc.Authorization to prevent double-generation of
    AddZeroAllocAuthorization() (CS0121 ambiguous call at consumer build).

    The @(Analyzer->WithMetadataValue(...)) item-function form is required:
    Condition="'%(Analyzer.NuGetPackageId)' == '...'" does not survive the
    SDK's analyzer item flow reliably (validated against the ZA.Templates
    za-clean workaround).
  -->
  <Target Name="ZeroAllocAuthorization_RemoveBundledAnalyzerWhenStandalonePresent"
          BeforeTargets="CoreCompile">
    <ItemGroup Condition="'@(Analyzer->WithMetadataValue('NuGetPackageId','ZeroAlloc.Authorization.Generator')->Count())' != '0'">
      <Analyzer Remove="@(Analyzer->WithMetadataValue('NuGetPackageId','ZeroAlloc.Authorization'))" />
    </ItemGroup>
  </Target>
</Project>
```

**Step 2: Write the empty `build/` stub**

Create `src/ZeroAlloc.Authorization/build/ZeroAlloc.Authorization.targets`:

```xml
<Project>
  <!--
    Intentionally empty. Some older SDK + NuGet combos warn when a package
    ships buildTransitive/ without a sibling build/ file. The actual guard
    target lives in buildTransitive/ZeroAlloc.Authorization.targets so it
    runs for direct and transitive consumers alike.
  -->
</Project>
```

**Step 3: Update the csproj to pack both folders**

Modify `src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj`. Add this ItemGroup before the closing `</Project>` tag, alongside the existing `IncludeGeneratorInPackage` target:

```xml
  <ItemGroup>
    <!-- Pack the buildTransitive guard target into the package so NuGet
         auto-imports it into every consumer. The build/ stub silences
         the matching-folder warning on older SDK/NuGet combos. -->
    <None Include="build\ZeroAlloc.Authorization.targets"
          Pack="true"
          PackagePath="build\ZeroAlloc.Authorization.targets" />
    <None Include="buildTransitive\ZeroAlloc.Authorization.targets"
          Pack="true"
          PackagePath="buildTransitive\ZeroAlloc.Authorization.targets" />
  </ItemGroup>
```

**Step 4: Pack and inspect**

```bash
dotnet pack src/ZeroAlloc.Authorization -c Release -p:PackageVersion=2.0.1-test -o /tmp/za-auth-pack-test
unzip -l /tmp/za-auth-pack-test/ZeroAlloc.Authorization.2.0.1-test.nupkg | grep -E "build/|buildTransitive/"
```

Expected:
- `build/ZeroAlloc.Authorization.targets` present
- `buildTransitive/ZeroAlloc.Authorization.targets` present
- `analyzers/dotnet/cs/ZeroAlloc.Authorization.Generator.dll` still present (backward-compat bundling)

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Authorization/build src/ZeroAlloc.Authorization/buildTransitive src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj
git commit -m "feat(pack): ship buildTransitive analyzer-leak guard

Adds buildTransitive/ZeroAlloc.Authorization.targets that auto-removes
the bundled analyzer at CoreCompile when ZeroAlloc.Authorization.Generator
is also referenced. Prevents double-generation of AddZeroAllocAuthorization()
for consumers who follow the new split-package pattern, while preserving
backward compat for 2.0.0-style consumers that only reference the main
package."
```

---

## Task 3: Pack-integrity smoke test

This is the load-bearing test. It validates the actual user-visible behavior: the ZA.Templates `za-clean` shape builds without CS0121 and without an analyzer-leak workaround.

**Files:**
- Create: `tests/ZeroAlloc.Authorization.PackSmoke/ZeroAlloc.Authorization.PackSmoke.csproj`
- Create: `tests/ZeroAlloc.Authorization.PackSmoke/PackSmokeTests.cs`
- Modify: `ZeroAlloc.Authorization.slnx` (add new test project)

**Step 1: Write the failing test (xUnit)**

Create `tests/ZeroAlloc.Authorization.PackSmoke/ZeroAlloc.Authorization.PackSmoke.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <!-- This test packs and consumes real .nupkg files; treat as an
         integration test and exclude from default xUnit collection
         parallelism so the temp NuGet feed isn't churned. -->
    <ParallelizeTestCollections>false</ParallelizeTestCollections>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
</Project>
```

Create `tests/ZeroAlloc.Authorization.PackSmoke/PackSmokeTests.cs`:

```csharp
using System.Diagnostics;
using System.Text;
using Xunit;

namespace ZeroAlloc.Authorization.PackSmoke;

/// <summary>
/// End-to-end pack + consume smoke test. Packs ZeroAlloc.Authorization and
/// ZeroAlloc.Authorization.Generator from source into a temp local NuGet feed,
/// scaffolds three throwaway projects that mirror the ZA.Templates za-clean
/// shape, then asserts the downstream build does NOT hit CS0121 (analyzer
/// leak across ProjectReference).
/// </summary>
public sealed class PackSmokeTests : IDisposable
{
    private const string TestVersion = "9.9.9-packsmoke";
    private readonly string _workDir;
    private readonly string _feed;

    public PackSmokeTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"za-auth-packsmoke-{Guid.NewGuid():N}");
        _feed = Path.Combine(_workDir, "feed");
        Directory.CreateDirectory(_feed);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Split_package_pattern_builds_without_CS0121()
    {
        var repoRoot = LocateRepoRoot();

        // Pack both packages into the temp feed.
        PackProject(Path.Combine(repoRoot, "src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj"));
        PackProject(Path.Combine(repoRoot, "src/ZeroAlloc.Authorization.Generator/ZeroAlloc.Authorization.Generator.csproj"));

        // Scaffold a three-project solution mirroring template shape.
        ScaffoldTemplate(useStandaloneGenerator: true);

        var build = RunDotnet($"build TestApp.sln -c Release", _workDir);
        Assert.True(build.ExitCode == 0,
            $"Expected build to succeed; got CS0121 or other error.\nSTDOUT:\n{build.StdOut}\nSTDERR:\n{build.StdErr}");
        Assert.DoesNotContain("CS0121", build.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Bundled_only_pattern_still_works_for_2_0_0_consumers()
    {
        var repoRoot = LocateRepoRoot();
        PackProject(Path.Combine(repoRoot, "src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj"));

        // Scaffold with only the main package referenced (2.0.0-style).
        // Application owns the [Policy]; no downstream Api project at all in
        // this scenario — the leak only occurs when there IS a downstream
        // project. We just assert the generator still runs in Application.
        ScaffoldTemplate(useStandaloneGenerator: false);

        var build = RunDotnet($"build TestApp.sln -c Release", _workDir);
        Assert.True(build.ExitCode == 0,
            $"Backward-compat scenario must still build.\nSTDOUT:\n{build.StdOut}\nSTDERR:\n{build.StdErr}");

        // Confirm the generator actually ran in Application (emitted file present).
        var generated = Directory.GetFiles(
            Path.Combine(_workDir, "src/TestApp.Application/obj/Release/net10.0/generated"),
            "*ZeroAllocAuthorization.Generated*.cs",
            SearchOption.AllDirectories);
        Assert.NotEmpty(generated);
    }

    private void PackProject(string csproj)
    {
        var result = RunDotnet(
            $"pack \"{csproj}\" -c Release -p:PackageVersion={TestVersion} -o \"{_feed}\"",
            Environment.CurrentDirectory);
        Assert.True(result.ExitCode == 0, $"Pack failed for {csproj}:\n{result.StdOut}\n{result.StdErr}");
    }

    private void ScaffoldTemplate(bool useStandaloneGenerator)
    {
        var src = Path.Combine(_workDir, "src");
        Directory.CreateDirectory(src);

        // NuGet.config pointing at the temp feed (plus nuget.org for transitive deps).
        File.WriteAllText(Path.Combine(_workDir, "NuGet.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{_feed}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        // Application project — declares [Policy] and [RequirePolicy].
        var appDir = Path.Combine(src, "TestApp.Application");
        Directory.CreateDirectory(appDir);
        var generatorRef = useStandaloneGenerator
            ? $"""<PackageReference Include="ZeroAlloc.Authorization.Generator" Version="{TestVersion}" PrivateAssets="all" />"""
            : "";
        File.WriteAllText(Path.Combine(appDir, "TestApp.Application.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="ZeroAlloc.Authorization" Version="{TestVersion}" />
                {generatorRef}
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(appDir, "TestPolicy.cs"), """
            using ZeroAlloc.Authorization;
            using ZeroAlloc.Results;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp.Application;

            [Policy("TestPolicy")]
            public sealed class TestPolicy : IAuthorizationPolicy
            {
                public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
                    ISecurityContext ctx, CancellationToken ct)
                    => new(UnitResult<AuthorizationFailure>.Success());
            }

            [RequirePolicy("TestPolicy")]
            public sealed record TestCommand(int Value);
            """);

        if (!useStandaloneGenerator)
        {
            // Bundled-only scenario: no Api project, single-project solution.
            File.WriteAllText(Path.Combine(_workDir, "TestApp.sln"), GenerateSln(appOnly: true));
            return;
        }

        // Api project — pure consumer, no PackageReference, only ProjectReference.
        var apiDir = Path.Combine(src, "TestApp.Api");
        Directory.CreateDirectory(apiDir);
        File.WriteAllText(Path.Combine(apiDir, "TestApp.Api.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <OutputType>Library</OutputType>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\TestApp.Application\TestApp.Application.csproj" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(apiDir, "Wire.cs"), """
            using Microsoft.Extensions.DependencyInjection;
            using ZeroAlloc.Authorization.Generated;

            namespace TestApp.Api;

            public static class Wire
            {
                public static IServiceCollection AddTest(IServiceCollection s)
                    => s.AddZeroAllocAuthorization();
            }
            """);

        File.WriteAllText(Path.Combine(_workDir, "TestApp.sln"), GenerateSln(appOnly: false));
    }

    private static string GenerateSln(bool appOnly)
    {
        // Minimal SDK-style "solution" — actually we use .slnx wouldn't help
        // here because dotnet build accepts a directory or per-csproj path.
        // For cross-platform reliability, write a real .sln with random GUIDs.
        var appGuid = "{B0000000-0000-0000-0000-000000000001}";
        var apiGuid = "{B0000000-0000-0000-0000-000000000002}";
        var sb = new StringBuilder();
        sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        sb.AppendLine($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"TestApp.Application\", \"src\\TestApp.Application\\TestApp.Application.csproj\", \"{appGuid}\"");
        sb.AppendLine("EndProject");
        if (!appOnly)
        {
            sb.AppendLine($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"TestApp.Api\", \"src\\TestApp.Api\\TestApp.Api.csproj\", \"{apiGuid}\"");
            sb.AppendLine("EndProject");
        }
        sb.AppendLine("Global");
        sb.AppendLine("EndGlobal");
        return sb.ToString();
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ZeroAlloc.Authorization.slnx")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static (int ExitCode, string StdOut, string StdErr) RunDotnet(string args, string workingDir)
    {
        var psi = new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        var so = p.StandardOutput.ReadToEnd();
        var se = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, so, se);
    }
}
```

**Step 2: Add to slnx**

Modify `ZeroAlloc.Authorization.slnx` — add the new test project alongside the existing test entries.

```bash
# Inspect current structure first
cat ZeroAlloc.Authorization.slnx
```

Then add:
```xml
<Project Path="tests/ZeroAlloc.Authorization.PackSmoke/ZeroAlloc.Authorization.PackSmoke.csproj" />
```

**Step 3: Run the test (will fail until Tasks 1 + 2 are complete)**

```bash
dotnet test tests/ZeroAlloc.Authorization.PackSmoke -c Release --logger "console;verbosity=detailed"
```

Expected (after Tasks 1 + 2): both tests PASS.

**Step 4: Commit**

```bash
git add tests/ZeroAlloc.Authorization.PackSmoke ZeroAlloc.Authorization.slnx
git commit -m "test(pack): smoke test for analyzer-leak fix + backward-compat path

Packs both packages from source into a temp feed, scaffolds a three-project
solution mirroring the ZA.Templates za-clean shape, and asserts the
downstream Api project builds without CS0121. Second test verifies the
bundled-analyzer 2.0.0-style consumer path still works."
```

---

## Task 4: Configure release-please for the 2.0.1 patch

**Files:**
- Modify: `release-please-config.json`

**Step 1: Add the one-shot `release-as`**

Modify `release-please-config.json`:

```json
{
  "packages": {
    ".": {
      "release-type": "simple",
      "release-as": "2.0.1",
      "changelog-sections": [
        { "type": "feat",     "section": "Features" },
        { "type": "fix",      "section": "Bug Fixes" },
        { "type": "docs",     "section": "Documentation" },
        { "type": "refactor", "section": "Refactors" }
      ]
    }
  },
  "$schema": "https://raw.githubusercontent.com/googleapis/release-please/main/schemas/config.json"
}
```

`.release-please-manifest.json` is NOT touched — release-please updates it on the merge of its own PR.

**Step 2: Verify the publish workflow handles the new packable project**

Read `.github/workflows/publish-from-manifest.yml` — confirm it walks `src/*/*.csproj` (it does as of `ad31a11`). Since `ZeroAlloc.Authorization.Generator.csproj` now has `IsPackable=true`, it'll be picked up automatically. No workflow changes needed.

**Step 3: Commit**

```bash
git add release-please-config.json
git commit -m "chore(release): one-shot release-as 2.0.1 for generator-split patch

Forces release-please to land 2.0.1 (patch) for the analyzer-leak fix
instead of the default minor bump that a feat: commit would trigger.
Override is removed in a follow-up PR after the first publish verifies."
```

---

## Task 5: Open the PR and watch CI

**Step 1: Push the branch**

```bash
git push -u origin feat/generator-package-split-201
```

**Step 2: Open the PR**

```bash
gh pr create --title "feat: split ZeroAlloc.Authorization.Generator into a standalone package (2.0.1)" --body "$(cat <<'EOF'
## Summary
- New `ZeroAlloc.Authorization.Generator` 2.0.1 NuGet package — generator-only, `developmentDependency=true`.
- `ZeroAlloc.Authorization` 2.0.1 keeps the bundled analyzer for 2.0.0 backward compat, but ships `buildTransitive/ZeroAlloc.Authorization.targets` that auto-removes the bundled analyzer when the new Generator package is also referenced. No double-generation, no CS0121.
- Pack-integrity smoke test validates the ZA.Templates `za-clean` shape builds without the per-csproj MSBuild workaround currently in the template repo.

## Design
`docs/plans/2026-05-21-authorization-generator-split-design.md`

## Sequenced followups
1. ZA.Templates PR — drop the analyzer-leak workaround from `MyApp.Api.csproj` + `MyApp.Infrastructure.csproj`, add `PackageReference Include="ZeroAlloc.Authorization.Generator" PrivateAssets="all"` to `MyApp.Application.csproj`.
2. This repo: remove the one-shot `release-as: "2.0.1"` from `release-please-config.json` after the first publish verifies.

## Test plan
- [ ] CI green: build + tests + pack-smoke
- [ ] Manual: pack locally, install in ZA.Templates scratch branch, confirm template builds without the MSBuild Target workaround

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**Step 3: Watch CI**

```bash
gh pr checks --watch
```

Expected: build, test, pack-smoke all green.

**Step 4: If CI fails**

Diagnose via `gh run view <run-id> --log-failed`. Common expected failure modes:
- buildTransitive guard syntax issue — re-run `dotnet build` on synthetic projects locally, inspect `dotnet build -bl` binlog.
- Pack-smoke flakiness from concurrent NuGet feed access — `[Fact]` tests already run sequentially per-class in xUnit; if cross-class flakes appear, mark the test class with `[Collection("PackSmoke")]`.

---

## Verification checklist (before merge)

- [ ] Task 1: Generator project packs cleanly; .nupkg contains only `analyzers/dotnet/cs/...dll`, no lib/.
- [ ] Task 2: Main package .nupkg contains both `build/` and `buildTransitive/` targets files.
- [ ] Task 3: PackSmokeTests both pass locally and in CI.
- [ ] Task 4: release-please-config.json has `release-as: "2.0.1"`.
- [ ] Task 5: PR open, CI green.
- [ ] Manual: locally packed nupkgs install cleanly in a ZA.Templates scratch branch with the workaround removed.

## Out of scope (followups, separately tracked)

- Removing the bundled analyzer entirely from the main package — defer to 3.0 (breaking change).
- Removing the `release-as` override after first publish — small followup PR.
- ZA.Templates PR to drop the workaround — separate PR in a different repo, after 2.0.1 publishes.
