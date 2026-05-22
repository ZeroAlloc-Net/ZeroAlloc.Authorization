using System.Diagnostics;
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
    private readonly string _testVersion;
    private readonly string _workDir;
    private readonly string _feed;

    public PackSmokeTests()
    {
        // Unique per-run version so NuGet's global-packages cache cannot serve a
        // stale extract of a prior run's package (the cache keys on version, so
        // reusing "9.9.9-packsmoke" across iterations silently re-uses the OLD
        // package contents — including missing buildTransitive targets).
        _testVersion = $"9.9.9-packsmoke-{Guid.NewGuid():N}";
        _workDir = Path.Combine(Path.GetTempPath(), $"za-auth-packsmoke-{Guid.NewGuid():N}");
        _feed = Path.Combine(_workDir, "feed");
        Directory.CreateDirectory(_feed);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workDir, recursive: true);
        }
#pragma warning disable CA1031, ERP022, RCS1075 // best-effort cleanup: swallow any IO error
        catch (Exception ex)
        {
            // Best-effort cleanup; ignore failures (locked files, already gone, etc.).
            Debug.WriteLine($"PackSmoke cleanup failed: {ex}");
        }
#pragma warning restore CA1031, ERP022, RCS1075
    }

    [Fact]
    public void Split_package_pattern_builds_without_CS0121()
    {
        var repoRoot = LocateRepoRoot();

        PackProject(Path.Combine(repoRoot, "src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj"));
        PackProject(Path.Combine(repoRoot, "src/ZeroAlloc.Authorization.Generator/ZeroAlloc.Authorization.Generator.csproj"));

        ScaffoldTemplate(useStandaloneGenerator: true);

        // Build the Api project; it ProjectReferences the Application project, so both build.
        var apiCsproj = Path.Combine(_workDir, "src/TestApp.Api/TestApp.Api.csproj");
        var build = RunDotnet($"build \"{apiCsproj}\" -c Release", _workDir);
        Assert.True(build.ExitCode == 0,
            $"Expected build to succeed; got CS0121 or other error.\nSTDOUT:\n{build.StdOut}\nSTDERR:\n{build.StdErr}");
        Assert.DoesNotContain("CS0121", build.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Split_package_pattern_survives_assemblyversion_mismatch()
    {
        // Regression test for the 2.0.1 publish-pipeline bug. The bundled-in-main
        // analyzer was packed by release-please.yml with -p:Version=2.0.1 (high
        // AssemblyVersion). The standalone Generator package was packed by
        // publish-from-manifest.yml without -p:Version (default 1.0.0
        // AssemblyVersion). MSBuild's _HandlePackageFileConflicts target picked
        // the bundled (higher AssemblyVersion wins) and discarded the standalone.
        // The DisableBundled guard then removed the bundled too, leaving no
        // analyzer at all. The fix moves the guard to fire BEFORE
        // _HandlePackageFileConflicts so the bundled is gone before conflict
        // resolution sees it; the standalone survives regardless of which copy
        // would have won.
        var repoRoot = LocateRepoRoot();

        // Force a high AssemblyVersion on the bundled DLL (via main package)
        // and a low one on the standalone Generator. Reproduces production.
        PackProject(Path.Combine(repoRoot, "src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj"),
                    assemblyVersion: "9.9.9");
        PackProject(Path.Combine(repoRoot, "src/ZeroAlloc.Authorization.Generator/ZeroAlloc.Authorization.Generator.csproj"),
                    assemblyVersion: "1.0.0");

        ScaffoldTemplate(useStandaloneGenerator: true);

        var apiCsproj = Path.Combine(_workDir, "src/TestApp.Api/TestApp.Api.csproj");
        var build = RunDotnet($"build \"{apiCsproj}\" -c Release", _workDir);
        Assert.True(build.ExitCode == 0,
            $"Build must succeed regardless of AssemblyVersion ordering between bundled and standalone analyzers.\nSTDOUT:\n{build.StdOut}\nSTDERR:\n{build.StdErr}");
        Assert.DoesNotContain("CS0121", build.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0234", build.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Bundled_only_pattern_still_works_for_2_0_0_consumers()
    {
        var repoRoot = LocateRepoRoot();
        PackProject(Path.Combine(repoRoot, "src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj"));

        ScaffoldTemplate(useStandaloneGenerator: false);

        // TestApp.Application contains a Wire.cs that calls AddZeroAllocAuthorization() —
        // the generated extension. If the bundled analyzer fails to emit it, the build
        // fails. This is a stronger guarantee than asserting on a generated file on disk
        // (which could be empty / missing the expected API surface).
        var appCsproj = Path.Combine(_workDir, "src/TestApp.Application/TestApp.Application.csproj");
        var build = RunDotnet($"build \"{appCsproj}\" -c Release", _workDir);
        Assert.True(build.ExitCode == 0,
            $"Backward-compat scenario must still build.\nSTDOUT:\n{build.StdOut}\nSTDERR:\n{build.StdErr}");
    }

    private void PackProject(string csproj)
        => PackProject(csproj, assemblyVersion: null);

    private void PackProject(string csproj, string? assemblyVersion)
    {
        var versionArg = assemblyVersion is null ? "" : $" -p:Version={assemblyVersion}";
        var result = RunDotnet(
            $"pack \"{csproj}\" -c Release -p:PackageVersion={_testVersion}{versionArg} -o \"{_feed}\"",
            Environment.CurrentDirectory);
        Assert.True(result.ExitCode == 0, $"Pack failed for {csproj}:\n{result.StdOut}\n{result.StdErr}");
    }

    private void ScaffoldTemplate(bool useStandaloneGenerator)
    {
        var src = Path.Combine(_workDir, "src");
        Directory.CreateDirectory(src);

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

        // Split-package idiom (2.0.1+): consumer opts in via two MSBuild properties.
        //   * Directory.Build.props at the work-dir root sets
        //     ZeroAllocAuthorizationDisableBundledAnalyzer=true so the bundled-in-main analyzer
        //     is removed from @(Analyzer) in every project (Application AND Api).
        //   * TestApp.Application.csproj sets ZeroAllocAuthorizationOwnsPolicies=true so the
        //     standalone Generator analyzer keeps running there (where the [Policy] /
        //     [RequirePolicy] types live). Api does NOT set this property, so the standalone
        //     analyzer is filtered out of Api's compile — preventing the cross-project
        //     ProjectReference analyzer-propagation leak that causes CS0121.
        // Bundled-only scenario (2.0.0-style) writes no Directory.Build.props and sets no
        // property — the bundled analyzer runs in Application as before; backward-compat.
        if (useStandaloneGenerator)
        {
            File.WriteAllText(Path.Combine(_workDir, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <ZeroAllocAuthorizationDisableBundledAnalyzer>true</ZeroAllocAuthorizationDisableBundledAnalyzer>
                  </PropertyGroup>
                </Project>
                """);
        }

        var appDir = Path.Combine(src, "TestApp.Application");
        Directory.CreateDirectory(appDir);
        // No PrivateAssets="all" here: with the flipped flow model (no
        // DevelopmentDependency=true on the Generator package), NuGet flows
        // the Generator package transitively to Api. Api then imports the
        // Generator package's buildTransitive guard and filters the analyzer
        // out because Api does not set ZeroAllocAuthorizationOwnsPolicies.
        var generatorRef = useStandaloneGenerator
            ? $"""<PackageReference Include="ZeroAlloc.Authorization.Generator" Version="{_testVersion}" />"""
            : "";
        var ownsPoliciesProperty = useStandaloneGenerator
            ? "<ZeroAllocAuthorizationOwnsPolicies>true</ZeroAllocAuthorizationOwnsPolicies>"
            : "";
        File.WriteAllText(Path.Combine(appDir, "TestApp.Application.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <!-- Emit generator output to disk so the test can assert the generator activated. -->
                <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
                {ownsPoliciesProperty}
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="ZeroAlloc.Authorization" Version="{_testVersion}" />
                <!-- The generated AddZeroAllocAuthorization extension targets IServiceCollection; consumers must bring M.E.DI. -->
                <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
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
        // Wire.cs forces the build to actually link against the generated
        // AddZeroAllocAuthorization extension. If the generator silently produced
        // an empty file (wrong PackageId, wrong analyzer path, etc.), the call
        // here fails to compile — a stronger guarantee than a "file exists on
        // disk" assertion.
        File.WriteAllText(Path.Combine(appDir, "WireApplication.cs"), """
            using Microsoft.Extensions.DependencyInjection;
            using ZeroAlloc.Authorization.Generated;

            namespace TestApp.Application;

            public static class WireApplication
            {
                public static IServiceCollection AddTest(IServiceCollection s)
                    => s.AddZeroAllocAuthorization();
            }
            """);

        if (!useStandaloneGenerator)
        {
            return;
        }

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
