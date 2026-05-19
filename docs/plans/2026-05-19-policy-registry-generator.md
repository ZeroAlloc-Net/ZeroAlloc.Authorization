# ZA.Authorization v2 — Policy Registry Generator Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship `ZeroAlloc.Authorization` v2 — source-generated `AuthorizerFor<TRequest>` dispatchers, renamed attributes (`[Policy]` / `[RequirePolicy]`), async-only `IAuthorizationPolicy`, all bundled into one NuGet. Mediator.Authorization v5 follows in a separate plan once v2 is on NuGet.

**Architecture:** Add the new generator (`src/ZeroAlloc.Authorization.Generator/`) bundled into the main NuGet via the same pattern as ZA.Rest (PR #101). Generator emits one `AuthorizerFor<TRequest>` subclass per `[RequirePolicy]`-decorated type plus a `AddZeroAllocAuthorization()` DI extension. v1 attributes and the four-method `IAuthorizationPolicy` interface are deleted in the final breaking-change task. Sequencing is **additive-then-deletion**: every intermediate task leaves the build green.

**Tech Stack:** C# / .NET 10 (net8.0;net9.0;net10.0 multi-TFM), Roslyn `IIncrementalGenerator`, Verify.SourceGenerators (snapshot tests), Microsoft.CodeAnalysis.CSharp.Testing (diagnostic tests), `Microsoft.Extensions.DependencyInjection`, ZA.Results (`UnitResult<TError>`), release-please for SemVer 2.0.0 bump.

**Reference design:** [2026-05-19-policy-registry-generator-design.md](2026-05-19-policy-registry-generator-design.md)

**Repo root for all paths below:** `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization/`

**Branch:** `feat/policy-registry-generator-v2` (already created and on HEAD `2ad20a8` — the committed design doc).

---

## Pre-flight notes for the executor

- **OS:** Windows. Use PowerShell. **Bash is NOT available.**
- **SDK pin:** `global.json` pins `10.0.300` with `rollForward: latestMinor`. Locally only `10.0.108`/`10.0.204` are installed. CI has `10.0.300+`.
  - **Option A (authorized for PR #101 series, repeat here):** edit `global.json` to `"version": "10.0.204"`, do the work, REVERT to `"10.0.300"` before pushing. The revert is a working-tree cleanup, not a commit.
  - **Option B:** trust CI for verification — write code, push without local `dotnet test`, fix on CI failures.
  - Recommend Option A for tight feedback loops. If the executor hits the SDK error on the first `dotnet` command, apply the workaround.
- **TDD discipline:** every task has a failing-test step BEFORE implementation. Don't skip it.
- **Commit per task.** No batching. Each commit message uses conventional-commits prefix (`feat:`, `feat!:`, `fix:`, `test:`, `docs:`, `chore:`) — release-please relies on this for v2.0.0 bump.

---

## Tasks at a glance

| # | Task | Breaking? |
|---|---|---|
| 0 | Pre-flight: verify environment + branch state | — |
| 1 | Add new `[Policy]` and `[RequirePolicy]` attributes (additive) | no |
| 2 | Add `AuthorizerFor<T>` abstract base | no |
| 3 | Scaffold `ZeroAlloc.Authorization.Generator` analyzer project | no |
| 4 | `PolicySymbolWalker` + `RequireSymbolWalker` (discovery) | no |
| 5 | `AuthorizerForEmitter` (per-request dispatcher) | no |
| 6 | `DIRegistrationEmitter` (`AddZeroAllocAuthorization`) | no |
| 7 | Wire `PolicyRegistryGenerator` entry point + first snapshot test | no |
| 8 | Cross-assembly discovery + cross-assembly snapshot test | no |
| 9 | Diagnostic ZAUTH001 — unknown policy name | no |
| 10 | Diagnostic ZAUTH002 — duplicate policy name | no |
| 11 | Diagnostic ZAUTH003 — `[Policy]` class doesn't implement `IAuthorizationPolicy` | no |
| 12 | Diagnostic ZAUTH004 — `[Policy]` class is abstract/static | no |
| 13 | Diagnostic ZAUTH005 — `[RequirePolicy]` on non-class type | no |
| 14 | AOT smoke scenario for new `[RequirePolicy]` flow | no |
| 15 | **Breaking:** rewrite `IAuthorizationPolicy` to async-only, update affected v1 tests | **yes** |
| 16 | **Breaking:** delete old `[AuthorizationPolicy]` and `[Authorize]` attributes | **yes** |
| 17 | Update `PublicAPI.Shipped.txt` (v2 surface) + release-please prep | **yes** |
| 18 | End-to-end verification + push + open PR | — |

---

### Task 0: Pre-flight

**Step 1: Confirm branch and clean tree.**

```powershell
Set-Location c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization
git branch --show-current
git status --short
```

Expected: branch `feat/policy-registry-generator-v2`, clean tree (only `?? artifacts/` or similar untracked, no `M` modified files).

**Step 2: Confirm dotnet works.**

```powershell
dotnet --version
```

If it fails with `Requested SDK version: 10.0.300`, apply the SDK workaround:

```powershell
# Read global.json
Get-Content global.json
# Edit "version": "10.0.300" → "10.0.204" using the Edit tool (not sed)
# Re-run:
dotnet --version
```

Expected after workaround: `10.0.204`.

**Step 3: Baseline build.**

```powershell
dotnet build -c Release
```

Expected: 0 errors. Note pre-existing warnings (don't try to fix them).

**Step 4: Baseline tests.**

```powershell
dotnet test -c Release
```

Expected: all tests pass. Capture the count — every subsequent task should keep this count green and add to it.

No commit for Task 0.

---

### Task 1: Add new attributes `[Policy]` and `[RequirePolicy]` (additive)

**Files:**
- Create: `src/ZeroAlloc.Authorization/PolicyAttribute.cs`
- Create: `src/ZeroAlloc.Authorization/RequirePolicyAttribute.cs`
- Create: `tests/ZeroAlloc.Authorization.Tests/PolicyAttributeTests.cs`
- Create: `tests/ZeroAlloc.Authorization.Tests/RequirePolicyAttributeTests.cs`
- Modify: `src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt` (add new types)

**Step 1: Write the failing test for `[Policy]`.**

Write `tests/ZeroAlloc.Authorization.Tests/PolicyAttributeTests.cs`:

```csharp
using Xunit;

namespace ZeroAlloc.Authorization.Tests;

public sealed class PolicyAttributeTests
{
    [Fact]
    public void PolicyAttribute_StoresName()
    {
        var attr = new PolicyAttribute("admin");
        Assert.Equal("admin", attr.Name);
    }

    [Fact]
    public void PolicyAttribute_AllowsClassTargetsOnly()
    {
        var usage = typeof(PolicyAttribute).GetCustomAttributes(typeof(System.AttributeUsageAttribute), false);
        Assert.Single(usage);
        var au = (System.AttributeUsageAttribute)usage[0];
        Assert.Equal(System.AttributeTargets.Class, au.ValidOn);
        Assert.False(au.AllowMultiple);
        Assert.False(au.Inherited);
    }
}
```

**Step 2: Run test — verify FAIL.**

```powershell
dotnet test --filter "FullyQualifiedName~PolicyAttributeTests" -c Release
```

Expected: compile error `'PolicyAttribute' does not exist`.

**Step 3: Implement `PolicyAttribute`.**

Write `src/ZeroAlloc.Authorization/PolicyAttribute.cs`:

```csharp
using System;

namespace ZeroAlloc.Authorization;

/// <summary>
/// Declares an authorization policy. The decorated class must implement
/// <see cref="IAuthorizationPolicy"/> and be reachable from the consumer's
/// compilation or referenced assemblies. The source generator emits a DI
/// registration for each <c>[Policy]</c>-decorated class.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PolicyAttribute : Attribute
{
    public PolicyAttribute(string name) => Name = name;
    public string Name { get; }
}
```

**Step 4: Write the failing test for `[RequirePolicy]`.**

Write `tests/ZeroAlloc.Authorization.Tests/RequirePolicyAttributeTests.cs`:

```csharp
using Xunit;

namespace ZeroAlloc.Authorization.Tests;

public sealed class RequirePolicyAttributeTests
{
    [Fact]
    public void RequirePolicyAttribute_StoresName()
    {
        var attr = new RequirePolicyAttribute("admin");
        Assert.Equal("admin", attr.PolicyName);
    }

    [Fact]
    public void RequirePolicyAttribute_AllowsClassTargetsOnly_AllowsMultiple()
    {
        var usage = typeof(RequirePolicyAttribute).GetCustomAttributes(typeof(System.AttributeUsageAttribute), false);
        Assert.Single(usage);
        var au = (System.AttributeUsageAttribute)usage[0];
        Assert.Equal(System.AttributeTargets.Class | System.AttributeTargets.Struct, au.ValidOn);
        Assert.True(au.AllowMultiple);
        Assert.False(au.Inherited);
    }
}
```

(Note: `AllowMultiple = true` because a single request can require multiple policies, e.g. `[RequirePolicy("admin")] [RequirePolicy("two-factor")]`.)

**Step 5: Run — verify FAIL.**

```powershell
dotnet test --filter "FullyQualifiedName~RequirePolicyAttributeTests" -c Release
```

Expected: compile error `'RequirePolicyAttribute' does not exist`.

**Step 6: Implement `RequirePolicyAttribute`.**

Write `src/ZeroAlloc.Authorization/RequirePolicyAttribute.cs`:

```csharp
using System;

namespace ZeroAlloc.Authorization;

/// <summary>
/// Marks a request type as requiring one or more authorization policies. The
/// named policy must be defined via <see cref="PolicyAttribute"/> somewhere
/// in the consumer's compilation or referenced assemblies. Stack the attribute
/// to require multiple policies (all must pass).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class RequirePolicyAttribute : Attribute
{
    public RequirePolicyAttribute(string policyName) => PolicyName = policyName;
    public string PolicyName { get; }
}
```

**Step 7: Update `PublicAPI.Unshipped.txt`.**

Append to `src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt`:

```
ZeroAlloc.Authorization.PolicyAttribute
ZeroAlloc.Authorization.PolicyAttribute.PolicyAttribute(string! name) -> void
ZeroAlloc.Authorization.PolicyAttribute.Name.get -> string!
ZeroAlloc.Authorization.RequirePolicyAttribute
ZeroAlloc.Authorization.RequirePolicyAttribute.RequirePolicyAttribute(string! policyName) -> void
ZeroAlloc.Authorization.RequirePolicyAttribute.PolicyName.get -> string!
```

**Step 8: Run all tests — verify GREEN.**

```powershell
dotnet test -c Release
```

Expected: previous baseline + 4 new tests, all green.

**Step 9: Commit.**

```powershell
git add src/ZeroAlloc.Authorization/PolicyAttribute.cs src/ZeroAlloc.Authorization/RequirePolicyAttribute.cs src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt tests/ZeroAlloc.Authorization.Tests/PolicyAttributeTests.cs tests/ZeroAlloc.Authorization.Tests/RequirePolicyAttributeTests.cs
git commit -m "feat: add [Policy] and [RequirePolicy] attributes (additive for v2)"
```

---

### Task 2: Add `AuthorizerFor<T>` abstract base

**Files:**
- Create: `src/ZeroAlloc.Authorization/AuthorizerFor.cs`
- Create: `tests/ZeroAlloc.Authorization.Tests/AuthorizerForTests.cs`
- Modify: `src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt`

**Step 1: Write the failing test.**

Write `tests/ZeroAlloc.Authorization.Tests/AuthorizerForTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization.Tests;

public sealed class AuthorizerForTests
{
    private sealed record FakeRequest(int Id);

    private sealed class AlwaysSucceedingAuthorizer : AuthorizerFor<FakeRequest>
    {
        public override ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
            ISecurityContext ctx, CancellationToken ct = default)
            => new(UnitResult.Success<AuthorizationFailure>());
    }

    [Fact]
    public async Task AuthorizerFor_Subclass_CanReturnSuccess()
    {
        var authorizer = new AlwaysSucceedingAuthorizer();
        var result = await authorizer.EvaluateAsync(AnonymousSecurityContext.Instance);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void AuthorizerFor_IsAbstract()
    {
        Assert.True(typeof(AuthorizerFor<FakeRequest>).IsAbstract);
    }
}
```

**Step 2: Run — verify FAIL.**

```powershell
dotnet test --filter "FullyQualifiedName~AuthorizerForTests" -c Release
```

Expected: compile error `'AuthorizerFor<>' does not exist`.

**Step 3: Implement `AuthorizerFor<T>`.**

Write `src/ZeroAlloc.Authorization/AuthorizerFor.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization;

/// <summary>
/// Per-request authorization dispatcher. The source generator emits one
/// concrete subclass per <see cref="RequirePolicyAttribute"/>-decorated type.
/// Consumers resolve via <c>IServiceProvider.GetService&lt;AuthorizerFor&lt;TRequest&gt;&gt;()</c>.
/// </summary>
/// <typeparam name="TRequest">The request type whose policies this dispatcher evaluates.</typeparam>
public abstract class AuthorizerFor<TRequest>
{
    public abstract ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default);
}
```

**Step 4: Update `PublicAPI.Unshipped.txt`.**

Append:

```
ZeroAlloc.Authorization.AuthorizerFor<TRequest>
ZeroAlloc.Authorization.AuthorizerFor<TRequest>.AuthorizerFor() -> void
abstract ZeroAlloc.Authorization.AuthorizerFor<TRequest>.EvaluateAsync(ZeroAlloc.Authorization.ISecurityContext! ctx, System.Threading.CancellationToken ct = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.UnitResult<ZeroAlloc.Authorization.AuthorizationFailure>>
```

**Step 5: Run all tests — verify GREEN.**

```powershell
dotnet test -c Release
```

**Step 6: Commit.**

```powershell
git add src/ZeroAlloc.Authorization/AuthorizerFor.cs src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt tests/ZeroAlloc.Authorization.Tests/AuthorizerForTests.cs
git commit -m "feat: add AuthorizerFor<TRequest> abstract base for generated dispatchers"
```

---

### Task 3: Scaffold `ZeroAlloc.Authorization.Generator` analyzer project

**Files:**
- Create: `src/ZeroAlloc.Authorization.Generator/ZeroAlloc.Authorization.Generator.csproj`
- Create: `src/ZeroAlloc.Authorization.Generator/PolicyRegistryGenerator.cs` (empty `IIncrementalGenerator` skeleton)
- Modify: `src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj` (add ProjectReference with `OutputItemType=Analyzer` + pack inclusion, mirror PR #101 / ZA.Rest pattern)

**Step 1: Look at the ZA.Rest pattern to ensure consistency.**

Read `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Rest/src/ZeroAlloc.Rest/ZeroAlloc.Rest.csproj` to see the `<ProjectReference OutputItemType="Analyzer" ReferenceOutputAssembly="false" PrivateAssets="all" />` + `IncludeGeneratorInPackage` target pattern. Replicate that shape here.

**Step 2: Create the generator csproj.**

Write `src/ZeroAlloc.Authorization.Generator/ZeroAlloc.Authorization.Generator.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <IsRoslynComponent>true</IsRoslynComponent>
    <IsPackable>false</IsPackable>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

(Versions come from `Directory.Packages.props` via Central Package Management. If they aren't pinned there, copy the exact versions from `ZeroAlloc.Rest.Generator.csproj`.)

**Step 3: Create the empty generator skeleton.**

Write `src/ZeroAlloc.Authorization.Generator/PolicyRegistryGenerator.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class PolicyRegistryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Pipeline wiring lands in Task 7.
    }
}
```

**Step 4: Wire the generator into the main package.**

Modify `src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj`. Add a new `<ItemGroup>` referencing the generator project as an analyzer + a target to bundle the generator DLL into the nupkg (copy the structure from `ZeroAlloc.Rest.csproj`):

```xml
<ItemGroup>
  <ProjectReference Include="..\ZeroAlloc.Authorization.Generator\ZeroAlloc.Authorization.Generator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false"
                    PrivateAssets="all" />
</ItemGroup>

<Target Name="IncludeGeneratorInPackage" BeforeTargets="_GetPackageFiles">
  <ItemGroup>
    <None Include="$(MSBuildThisFileDirectory)..\ZeroAlloc.Authorization.Generator\bin\$(Configuration)\netstandard2.0\ZeroAlloc.Authorization.Generator.dll"
          Pack="true"
          PackagePath="analyzers/dotnet/cs"
          Visible="false" />
  </ItemGroup>
</Target>
```

(Place this section before the closing `</Project>`. Don't modify any existing `<ItemGroup>` — the new one is purely additive.)

**Step 5: Add the generator project to the slnx solution.**

```powershell
dotnet sln c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization/ZeroAlloc.Authorization.slnx add src/ZeroAlloc.Authorization.Generator/ZeroAlloc.Authorization.Generator.csproj
```

(If the repo uses `.sln` instead, adjust accordingly. Check via `Get-ChildItem *.sln*` first.)

**Step 6: Build to verify scaffolding compiles.**

```powershell
dotnet build -c Release
```

Expected: 0 errors. The generator project produces a netstandard2.0 DLL; the main library still targets multi-TFM (net8.0/net9.0/net10.0).

**Step 7: Pack to confirm the generator ships inside the nupkg.**

```powershell
dotnet pack src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj -c Release -o artifacts/local
$nupkg = Get-ChildItem artifacts/local/ZeroAlloc.Authorization.*.nupkg | Select-Object -First 1
$tmp = Join-Path $env:TEMP "za-auth-inspect-$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $tmp | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($nupkg.FullName, $tmp)
Get-ChildItem -Path $tmp -Recurse -File | ForEach-Object { $_.FullName.Substring($tmp.Length + 1) }
Remove-Item -Recurse -Force $tmp
```

Expected: among the listed files, `analyzers/dotnet/cs/ZeroAlloc.Authorization.Generator.dll` exists.

**Step 8: Commit.**

```powershell
git add src/ZeroAlloc.Authorization.Generator/ src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj ZeroAlloc.Authorization.slnx
git commit -m "build: scaffold ZeroAlloc.Authorization.Generator analyzer project (bundled into main nupkg)"
```

---

### Task 4: `PolicySymbolWalker` + `RequireSymbolWalker`

**Files:**
- Create: `src/ZeroAlloc.Authorization.Generator/Discovery/PolicySymbolWalker.cs`
- Create: `src/ZeroAlloc.Authorization.Generator/Discovery/RequireSymbolWalker.cs`
- Create: `src/ZeroAlloc.Authorization.Generator/Discovery/PolicyInfo.cs` (data record)
- Create: `src/ZeroAlloc.Authorization.Generator/Discovery/RequireInfo.cs` (data record)

These walkers run inside the IIncrementalGenerator pipeline. They take a `Compilation` (single-compilation discovery only for this task — cross-assembly arrives in Task 8) and return collections of `PolicyInfo` and `RequireInfo`.

**Step 1: Write data records.**

`Discovery/PolicyInfo.cs`:

```csharp
namespace ZeroAlloc.Authorization.Generator.Discovery;

internal sealed record PolicyInfo(
    string Name,                 // "admin"
    string FullyQualifiedTypeName, // "MyApp.AdminPolicy"
    bool IsInstantiable);          // false if abstract/static (drives ZAUTH004)
```

`Discovery/RequireInfo.cs`:

```csharp
namespace ZeroAlloc.Authorization.Generator.Discovery;

internal sealed record RequireInfo(
    string FullyQualifiedTypeName, // "MyApp.DeleteUser"
    string SafeIdentifier,         // "MyApp_DeleteUser" — for generated class name
    System.Collections.Generic.IReadOnlyList<string> PolicyNames); // ["admin", "two-factor"]
```

**Step 2: Write the walkers (compilation-only for now).**

`Discovery/PolicySymbolWalker.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Discovery;

internal static class PolicySymbolWalker
{
    private const string PolicyAttributeFullName = "ZeroAlloc.Authorization.PolicyAttribute";
    private const string AuthorizationPolicyInterfaceFullName = "ZeroAlloc.Authorization.IAuthorizationPolicy";

    public static IReadOnlyList<PolicyInfo> Find(Compilation compilation)
    {
        var policyAttr = compilation.GetTypeByMetadataName(PolicyAttributeFullName);
        var policyIface = compilation.GetTypeByMetadataName(AuthorizationPolicyInterfaceFullName);
        if (policyAttr is null || policyIface is null) return System.Array.Empty<PolicyInfo>();

        var results = new List<PolicyInfo>();
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(compilation.SourceModule.GlobalNamespace);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var member in current.GetMembers())
            {
                if (member is INamespaceSymbol ns) stack.Push(ns);
                else if (member is INamedTypeSymbol type)
                {
                    foreach (var nested in type.GetTypeMembers()) stack.Push(nested);
                    var attr = type.GetAttributes()
                        .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, policyAttr));
                    if (attr is null) continue;
                    if (attr.ConstructorArguments.Length == 0) continue;
                    var nameArg = attr.ConstructorArguments[0];
                    if (nameArg.Value is not string name) continue;
                    var instantiable = !type.IsAbstract && !type.IsStatic;
                    results.Add(new PolicyInfo(
                        name,
                        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        instantiable));
                }
            }
        }
        return results;
    }
}
```

`Discovery/RequireSymbolWalker.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Discovery;

internal static class RequireSymbolWalker
{
    private const string RequirePolicyAttributeFullName = "ZeroAlloc.Authorization.RequirePolicyAttribute";

    public static IReadOnlyList<RequireInfo> Find(Compilation compilation)
    {
        var requireAttr = compilation.GetTypeByMetadataName(RequirePolicyAttributeFullName);
        if (requireAttr is null) return System.Array.Empty<RequireInfo>();

        var results = new List<RequireInfo>();
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(compilation.SourceModule.GlobalNamespace);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var member in current.GetMembers())
            {
                if (member is INamespaceSymbol ns) stack.Push(ns);
                else if (member is INamedTypeSymbol type)
                {
                    foreach (var nested in type.GetTypeMembers()) stack.Push(nested);
                    var attrs = type.GetAttributes()
                        .Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, requireAttr))
                        .ToArray();
                    if (attrs.Length == 0) continue;
                    var names = attrs
                        .Select(a => a.ConstructorArguments.Length > 0 ? a.ConstructorArguments[0].Value as string : null)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .Cast<string>()
                        .ToArray();
                    if (names.Length == 0) continue;
                    var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var safe = type.ToDisplayString(new SymbolDisplayFormat(
                        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces))
                        .Replace('.', '_')
                        .Replace('+', '_');
                    results.Add(new RequireInfo(fqn, safe, names));
                }
            }
        }
        return results;
    }
}
```

**Step 3: Build to verify compilation.**

```powershell
dotnet build src/ZeroAlloc.Authorization.Generator/ -c Release
```

Expected: 0 errors.

**Step 4: Commit.**

```powershell
git add src/ZeroAlloc.Authorization.Generator/Discovery/
git commit -m "feat: add policy and require-policy symbol walkers (single-compilation discovery)"
```

(No tests yet — they arrive in Task 7 when the generator wires everything together. The walkers are internal infrastructure.)

---

### Task 5: `AuthorizerForEmitter`

**Files:**
- Create: `src/ZeroAlloc.Authorization.Generator/Emit/AuthorizerForEmitter.cs`

The emitter produces one `GeneratedAuthorizerFor_<safe-identifier>` class per `RequireInfo`. The class extends `AuthorizerFor<TRequest>` and overrides `EvaluateAsync` to short-circuit through each policy in order.

**Step 1: Implement the emitter.**

Write `src/ZeroAlloc.Authorization.Generator/Emit/AuthorizerForEmitter.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ZeroAlloc.Authorization.Generator.Discovery;

namespace ZeroAlloc.Authorization.Generator.Emit;

internal static class AuthorizerForEmitter
{
    public static string Emit(
        IReadOnlyList<RequireInfo> requires,
        IReadOnlyDictionary<string, PolicyInfo> policiesByName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace ZeroAlloc.Authorization.Generated");
        sb.AppendLine("{");
        foreach (var req in requires)
        {
            EmitOne(sb, req, policiesByName);
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitOne(
        StringBuilder sb,
        RequireInfo req,
        IReadOnlyDictionary<string, PolicyInfo> policiesByName)
    {
        sb.AppendLine($"    internal sealed class GeneratedAuthorizerFor_{req.SafeIdentifier}");
        sb.AppendLine($"        : global::ZeroAlloc.Authorization.AuthorizerFor<{req.FullyQualifiedTypeName}>");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly global::System.IServiceProvider _sp;");
        sb.AppendLine($"        public GeneratedAuthorizerFor_{req.SafeIdentifier}(global::System.IServiceProvider sp) => _sp = sp;");
        sb.AppendLine();
        sb.AppendLine("        public override async global::System.Threading.Tasks.ValueTask<global::ZeroAlloc.Results.UnitResult<global::ZeroAlloc.Authorization.AuthorizationFailure>> EvaluateAsync(");
        sb.AppendLine("            global::ZeroAlloc.Authorization.ISecurityContext ctx,");
        sb.AppendLine("            global::System.Threading.CancellationToken ct = default)");
        sb.AppendLine("        {");
        foreach (var name in req.PolicyNames)
        {
            if (!policiesByName.TryGetValue(name, out var policy)) continue; // ZAUTH001 will have already errored
            sb.AppendLine($"            var __p_{Sanitize(name)} = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{policy.FullyQualifiedTypeName}>(_sp);");
            sb.AppendLine($"            var __r_{Sanitize(name)} = await __p_{Sanitize(name)}.EvaluateAsync(ctx, ct).ConfigureAwait(false);");
            sb.AppendLine($"            if (__r_{Sanitize(name)}.IsFailure) return __r_{Sanitize(name)};");
        }
        sb.AppendLine("            return global::ZeroAlloc.Results.UnitResult.Success<global::ZeroAlloc.Authorization.AuthorizationFailure>();");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }
}
```

**Step 2: Build to verify.**

```powershell
dotnet build src/ZeroAlloc.Authorization.Generator/ -c Release
```

Expected: 0 errors.

**Step 3: Commit.**

```powershell
git add src/ZeroAlloc.Authorization.Generator/Emit/AuthorizerForEmitter.cs
git commit -m "feat: emit AuthorizerFor<T> subclass per [RequirePolicy]-decorated type"
```

---

### Task 6: `DIRegistrationEmitter`

**Files:**
- Create: `src/ZeroAlloc.Authorization.Generator/Emit/DIRegistrationEmitter.cs`

**Step 1: Implement.**

Write `src/ZeroAlloc.Authorization.Generator/Emit/DIRegistrationEmitter.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using ZeroAlloc.Authorization.Generator.Discovery;

namespace ZeroAlloc.Authorization.Generator.Emit;

internal static class DIRegistrationEmitter
{
    public static string Emit(
        IReadOnlyList<PolicyInfo> policies,
        IReadOnlyList<RequireInfo> requires)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace ZeroAlloc.Authorization.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    public static class GeneratedAuthorizationRegistration");
        sb.AppendLine("    {");
        sb.AppendLine("        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddZeroAllocAuthorization(");
        sb.AppendLine("            this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("        {");
        foreach (var policy in policies)
        {
            if (!policy.IsInstantiable) continue;
            sb.AppendLine($"            global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<{policy.FullyQualifiedTypeName}>(services);");
        }
        foreach (var req in requires)
        {
            sb.AppendLine($"            global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<global::ZeroAlloc.Authorization.AuthorizerFor<{req.FullyQualifiedTypeName}>, global::ZeroAlloc.Authorization.Generated.GeneratedAuthorizerFor_{req.SafeIdentifier}>(services);");
        }
        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
```

**Step 2: Build.**

```powershell
dotnet build src/ZeroAlloc.Authorization.Generator/ -c Release
```

**Step 3: Commit.**

```powershell
git add src/ZeroAlloc.Authorization.Generator/Emit/DIRegistrationEmitter.cs
git commit -m "feat: emit AddZeroAllocAuthorization DI registration extension"
```

---

### Task 7: Wire `PolicyRegistryGenerator` + first snapshot test

**Files:**
- Modify: `src/ZeroAlloc.Authorization.Generator/PolicyRegistryGenerator.cs` (wire up walkers + emitters in the IIncrementalGenerator pipeline)
- Create: `tests/ZeroAlloc.Authorization.Generator.Tests/ZeroAlloc.Authorization.Generator.Tests.csproj` (snapshot test project)
- Create: `tests/ZeroAlloc.Authorization.Generator.Tests/GeneratorSnapshotTests.cs`
- Create: `tests/ZeroAlloc.Authorization.Generator.Tests/Snapshots/Basic.verified.cs` (the expected output — will be auto-generated by Verify on first run)

**Step 1: Wire the generator entry point.**

Rewrite `src/ZeroAlloc.Authorization.Generator/PolicyRegistryGenerator.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ZeroAlloc.Authorization.Generator.Discovery;
using ZeroAlloc.Authorization.Generator.Emit;

namespace ZeroAlloc.Authorization.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class PolicyRegistryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var compilationProvider = context.CompilationProvider;
        context.RegisterSourceOutput(compilationProvider, GenerateForCompilation);
    }

    private static void GenerateForCompilation(SourceProductionContext spc, Compilation compilation)
    {
        var policies = PolicySymbolWalker.Find(compilation);
        var requires = RequireSymbolWalker.Find(compilation);
        if (policies.Count == 0 && requires.Count == 0) return;

        var byName = policies
            .GroupBy(p => p.Name)
            .ToDictionary(g => g.Key, g => g.First());  // ZAUTH002 (duplicates) will be diagnosed in Task 10

        var authorizers = AuthorizerForEmitter.Emit(requires, byName);
        var registration = DIRegistrationEmitter.Emit(policies, requires);

        spc.AddSource("ZeroAllocAuthorization.Generated.g.cs", SourceText.From(authorizers + registration, System.Text.Encoding.UTF8));
    }
}
```

**Step 2: Scaffold the snapshot test project.**

Write `tests/ZeroAlloc.Authorization.Generator.Tests/ZeroAlloc.Authorization.Generator.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" />
    <PackageReference Include="Verify.Xunit" />
    <PackageReference Include="Verify.SourceGenerators" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ZeroAlloc.Authorization\ZeroAlloc.Authorization.csproj" />
    <ProjectReference Include="..\..\src\ZeroAlloc.Authorization.Generator\ZeroAlloc.Authorization.Generator.csproj" />
  </ItemGroup>
</Project>
```

(If `Verify.SourceGenerators` isn't already in `Directory.Packages.props`, add it there first. Match the version used in `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator/tests/ZeroAlloc.Mediator.Generator.Tests/`.)

Add the project to the solution:

```powershell
dotnet sln c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization/ZeroAlloc.Authorization.slnx add tests/ZeroAlloc.Authorization.Generator.Tests/ZeroAlloc.Authorization.Generator.Tests.csproj
```

**Step 3: Write the first snapshot test.**

Write `tests/ZeroAlloc.Authorization.Generator.Tests/GeneratorSnapshotTests.cs`:

```csharp
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VerifyXunit;
using Xunit;

namespace ZeroAlloc.Authorization.Generator.Tests;

[UsesVerify]
public sealed class GeneratorSnapshotTests
{
    private const string BasicSource = @"
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;
using System.Threading;
using System.Threading.Tasks;

namespace MyApp;

[Policy(""admin"")]
public sealed class AdminPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(UnitResult.Success<AuthorizationFailure>());
}

[RequirePolicy(""admin"")]
public sealed record DeleteUser(int Id);
";

    [Fact]
    public Task Basic_SinglePolicy_SingleRequirePolicy_Snapshot()
    {
        return RunAndVerify(BasicSource);
    }

    private static Task RunAndVerify(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.ValueTask).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ZeroAlloc.Authorization.IAuthorizationPolicy).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ZeroAlloc.Results.UnitResult).Assembly.Location),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new PolicyRegistryGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return Verifier.Verify(driver).UseDirectory("Snapshots");
    }
}
```

**Step 4: Run — first run creates the `.received.cs` file (snapshot doesn't exist yet → test fails).**

```powershell
dotnet test tests/ZeroAlloc.Authorization.Generator.Tests/ -c Release
```

Expected: 1 failure with Verify output indicating `Snapshots/GeneratorSnapshotTests.Basic_SinglePolicy_SingleRequirePolicy_Snapshot.received.cs` was written.

**Step 5: Inspect the received output.**

```powershell
Get-Content tests/ZeroAlloc.Authorization.Generator.Tests/Snapshots/*.received.cs
```

Expected: A `GeneratedAuthorizerFor_MyApp_DeleteUser` class extending `AuthorizerFor<MyApp.DeleteUser>` with one policy resolution, plus `GeneratedAuthorizationRegistration.AddZeroAllocAuthorization` with two `AddScoped` calls (the policy + the AuthorizerFor).

**Step 6: Accept the snapshot.**

Rename `*.received.cs` to `*.verified.cs`:

```powershell
$received = Get-ChildItem tests/ZeroAlloc.Authorization.Generator.Tests/Snapshots/*.received.cs
$verified = $received.FullName -replace '\.received\.cs$', '.verified.cs'
Move-Item -Force $received.FullName $verified
```

**Step 7: Re-run — expect PASS.**

```powershell
dotnet test tests/ZeroAlloc.Authorization.Generator.Tests/ -c Release
```

Expected: 1 test passing.

**Step 8: Commit.**

```powershell
git add src/ZeroAlloc.Authorization.Generator/PolicyRegistryGenerator.cs tests/ZeroAlloc.Authorization.Generator.Tests/ ZeroAlloc.Authorization.slnx
git commit -m "feat: wire PolicyRegistryGenerator pipeline + first snapshot test"
```

---

### Task 8: Cross-assembly discovery

Currently the walkers iterate only `compilation.SourceModule.GlobalNamespace`. Add a parallel walk of `compilation.SourceModule.ReferencedAssemblySymbols` so policies defined in a separate `.csproj` are discoverable.

**Files:**
- Modify: `src/ZeroAlloc.Authorization.Generator/Discovery/PolicySymbolWalker.cs`
- Modify: `src/ZeroAlloc.Authorization.Generator/Discovery/RequireSymbolWalker.cs`
- Modify: `tests/ZeroAlloc.Authorization.Generator.Tests/GeneratorSnapshotTests.cs` (add cross-assembly test)
- Create: `tests/ZeroAlloc.Authorization.Generator.Tests/Snapshots/CrossAssembly.verified.cs`

**Step 1: Refactor the walkers to walk both source AND referenced assemblies.**

In each walker, extract the "walk a namespace tree" logic into a helper, then call it for `compilation.SourceModule.GlobalNamespace` AND for each `IAssemblySymbol` in `compilation.SourceModule.ReferencedAssemblySymbols`'s `GlobalNamespace`.

Pattern (apply to both `PolicySymbolWalker` and `RequireSymbolWalker`):

```csharp
public static IReadOnlyList<PolicyInfo> Find(Compilation compilation)
{
    var policyAttr = compilation.GetTypeByMetadataName(PolicyAttributeFullName);
    var policyIface = compilation.GetTypeByMetadataName(AuthorizationPolicyInterfaceFullName);
    if (policyAttr is null || policyIface is null) return System.Array.Empty<PolicyInfo>();

    var results = new List<PolicyInfo>();
    WalkNamespace(compilation.SourceModule.GlobalNamespace, policyAttr, results);
    foreach (var refAsm in compilation.SourceModule.ReferencedAssemblySymbols)
    {
        WalkNamespace(refAsm.GlobalNamespace, policyAttr, results);
    }
    return results;
}

private static void WalkNamespace(INamespaceOrTypeSymbol root, INamedTypeSymbol attr, List<PolicyInfo> sink)
{
    var stack = new Stack<INamespaceOrTypeSymbol>();
    stack.Push(root);
    while (stack.Count > 0)
    {
        var current = stack.Pop();
        foreach (var member in current.GetMembers())
        {
            if (member is INamespaceSymbol ns) stack.Push(ns);
            else if (member is INamedTypeSymbol type)
            {
                // ...existing per-type logic
            }
        }
    }
}
```

**Step 2: Add the cross-assembly snapshot test.**

Append to `GeneratorSnapshotTests.cs`:

```csharp
[Fact]
public Task CrossAssembly_PolicyInReferencedAsm_RequireInSource_Snapshot()
{
    // First, build a "LibA" assembly that defines the policy
    var libASource = @"
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;
using System.Threading;
using System.Threading.Tasks;

namespace SharedKernel;

[Policy(""admin"")]
public sealed class AdminPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(UnitResult.Success<AuthorizationFailure>());
}
";
    var libACompilation = CSharpCompilation.Create(
        "SharedKernel",
        new[] { CSharpSyntaxTree.ParseText(libASource) },
        StandardReferences,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    using var libAStream = new System.IO.MemoryStream();
    var emitResult = libACompilation.Emit(libAStream);
    Assert.True(emitResult.Success);
    libAStream.Position = 0;
    var libARef = MetadataReference.CreateFromStream(libAStream);

    // Then build the consumer assembly that has the [RequirePolicy] usage, referencing LibA
    var consumerSource = @"
using ZeroAlloc.Authorization;

namespace MyApp;

[RequirePolicy(""admin"")]
public sealed record DeleteUser(int Id);
";
    var consumerCompilation = CSharpCompilation.Create(
        "Consumer",
        new[] { CSharpSyntaxTree.ParseText(consumerSource) },
        StandardReferences.Append(libARef),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    var generator = new PolicyRegistryGenerator().AsSourceGenerator();
    var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(consumerCompilation);
    return Verifier.Verify(driver).UseDirectory("Snapshots");
}

private static readonly MetadataReference[] StandardReferences = new[]
{
    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
    MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.ValueTask).Assembly.Location),
    MetadataReference.CreateFromFile(typeof(ZeroAlloc.Authorization.IAuthorizationPolicy).Assembly.Location),
    MetadataReference.CreateFromFile(typeof(ZeroAlloc.Results.UnitResult).Assembly.Location),
};
```

Also rewrite `RunAndVerify` to use `StandardReferences` for consistency.

**Step 3: Run — accept the new snapshot.**

```powershell
dotnet test tests/ZeroAlloc.Authorization.Generator.Tests/ --filter "CrossAssembly" -c Release
```

Expected: 1 failure (new `.received.cs` written). Inspect, rename to `.verified.cs`, re-run, expect PASS.

**Step 4: Run all tests — both snapshots green.**

```powershell
dotnet test tests/ZeroAlloc.Authorization.Generator.Tests/ -c Release
```

**Step 5: Commit.**

```powershell
git add src/ZeroAlloc.Authorization.Generator/Discovery/ tests/ZeroAlloc.Authorization.Generator.Tests/
git commit -m "feat: cross-assembly policy discovery (walk ReferencedAssemblySymbols)"
```

---

### Tasks 9-13: Diagnostics ZAUTH001-005

Each diagnostic follows the same shape: a `DiagnosticDescriptor` constant, a check inside `GenerateForCompilation`, a positive-and-negative test pair. I'll spell out Task 9 in full and abbreviate 10-13 since the pattern is identical.

#### Task 9: ZAUTH001 — `[RequirePolicy]` references unknown policy name

**Files:**
- Create: `src/ZeroAlloc.Authorization.Generator/Diagnostics/Descriptors.cs` (will hold all 5 descriptors over the next 5 tasks)
- Modify: `src/ZeroAlloc.Authorization.Generator/PolicyRegistryGenerator.cs`
- Create: `tests/ZeroAlloc.Authorization.Generator.Tests/DiagnosticTests.cs`

**Step 1: Write the failing test.**

Write `tests/ZeroAlloc.Authorization.Generator.Tests/DiagnosticTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Authorization.Generator.Tests;

public sealed class DiagnosticTests
{
    private static readonly MetadataReference[] References = new[]
    {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.ValueTask).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Authorization.IAuthorizationPolicy).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Results.UnitResult).Assembly.Location),
    };

    [Fact]
    public void ZAUTH001_UnknownPolicyName_FiresError()
    {
        var source = @"
using ZeroAlloc.Authorization;
namespace MyApp;
[RequirePolicy(""nonexistent"")]
public sealed record Foo(int Id);
";
        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "ZAUTH001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ZAUTH001_KnownPolicy_DoesNotFire()
    {
        var source = @"
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;
using System.Threading;
using System.Threading.Tasks;

namespace MyApp;

[Policy(""admin"")]
public sealed class AdminPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(UnitResult.Success<AuthorizationFailure>());
}

[RequirePolicy(""admin"")]
public sealed record Foo(int Id);
";
        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "ZAUTH001");
    }

    private static ImmutableArray<Diagnostic> RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var generator = new PolicyRegistryGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult().Diagnostics;
    }
}
```

**Step 2: Run — verify FAIL.**

```powershell
dotnet test tests/ZeroAlloc.Authorization.Generator.Tests/ --filter "ZAUTH001" -c Release
```

Expected: positive test fails (no ZAUTH001 emitted yet); negative test passes vacuously.

**Step 3: Add the descriptor.**

Create `src/ZeroAlloc.Authorization.Generator/Diagnostics/Descriptors.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Diagnostics;

internal static class Descriptors
{
    public static readonly DiagnosticDescriptor UnknownPolicyName = new(
        id: "ZAUTH001",
        title: "Unknown policy name",
        messageFormat: "Required policy '{0}' is not defined. Add [Policy(\"{0}\")] to a class implementing IAuthorizationPolicy.",
        category: "ZeroAlloc.Authorization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
```

**Step 4: Emit the diagnostic in the generator.**

Modify `GenerateForCompilation` in `PolicyRegistryGenerator.cs` — after building `byName`, iterate `requires` and emit ZAUTH001 for any policy name with no match:

```csharp
foreach (var req in requires)
{
    foreach (var name in req.PolicyNames)
    {
        if (!byName.ContainsKey(name))
        {
            // Note: location resolution to the actual attribute site is a future polish item.
            // For v1 of the diagnostic, emit at Location.None — surfaces in the build output
            // with the offending policy name in the message.
            spc.ReportDiagnostic(Diagnostic.Create(
                Descriptors.UnknownPolicyName, Location.None, name));
        }
    }
}
```

**Step 5: Re-run — verify GREEN.**

```powershell
dotnet test tests/ZeroAlloc.Authorization.Generator.Tests/ --filter "ZAUTH001" -c Release
```

Expected: both tests pass.

**Step 6: Run full generator-test suite — confirm snapshots still pass.**

```powershell
dotnet test tests/ZeroAlloc.Authorization.Generator.Tests/ -c Release
```

**Step 7: Commit.**

```powershell
git add src/ZeroAlloc.Authorization.Generator/Diagnostics/ src/ZeroAlloc.Authorization.Generator/PolicyRegistryGenerator.cs tests/ZeroAlloc.Authorization.Generator.Tests/DiagnosticTests.cs
git commit -m "feat: emit ZAUTH001 when [RequirePolicy] references unknown policy name"
```

#### Task 10: ZAUTH002 — duplicate `[Policy]` name

Same shape as Task 9 with:
- Descriptor: `DuplicatePolicyName` (ZAUTH002, error), message `"Duplicate policy name '{0}'. Each [Policy] name must be unique within the compilation."`
- Check: in `PolicySymbolWalker.Find`, after collecting all `PolicyInfo`s, group by `Name`; for groups with `Count > 1`, the generator emits ZAUTH002 once per duplicate group
- Tests: positive (two `[Policy("admin")]` classes → ZAUTH002 fires), negative (single `[Policy("admin")]` → does not fire)
- Commit: `feat: emit ZAUTH002 on duplicate [Policy] names`

#### Task 11: ZAUTH003 — `[Policy]` class doesn't implement `IAuthorizationPolicy`

- Descriptor: `PolicyClassDoesNotImplementInterface` (ZAUTH003, error)
- Check: in `PolicySymbolWalker.Find`, after finding a `[Policy]`-decorated type, verify `type.AllInterfaces.Contains(policyIface, SymbolEqualityComparer.Default)`. If not, emit ZAUTH003 and skip adding the policy to results.
- Tests: positive (`[Policy("admin")] class Foo { }` → ZAUTH003), negative (legitimate policy → does not fire)
- Commit: `feat: emit ZAUTH003 when [Policy] class does not implement IAuthorizationPolicy`

#### Task 12: ZAUTH004 — `[Policy]` class is abstract / static

- Descriptor: `PolicyClassNotInstantiable` (ZAUTH004, error)
- Check: the walker already flags `IsInstantiable = false`; in the generator, after walker returns results, emit ZAUTH004 for any `PolicyInfo` where `!IsInstantiable`. Skip adding non-instantiable policies to DI.
- Tests: positive (`[Policy("admin")] abstract class AdminPolicy : IAuthorizationPolicy { ... }` → ZAUTH004), negative
- Commit: `feat: emit ZAUTH004 when [Policy] class is abstract or static`

#### Task 13: ZAUTH005 — `[RequirePolicy]` on non-class type

- Descriptor: `RequirePolicyOnInvalidType` (ZAUTH005, error). Message: `"[RequirePolicy] can only be applied to classes, structs, or records. '{0}' is {1}."`
- Check: `RequireSymbolWalker` already filters to `INamedTypeSymbol` — but `AttributeTargets.Interface` could still slip through if the attribute usage isn't strict. Actually our `RequirePolicyAttribute` declares `AttributeTargets.Class | AttributeTargets.Struct`, so compiler already rejects interface usage. However, **method-level usage was a real concern** (Q3 design decision): if `AttributeTargets` allows method, the C# compiler accepts it; we need to explicitly disallow.

  Looking at the attribute spec in Task 1: we set `AttributeTargets.Class | AttributeTargets.Struct` — method targets are already rejected by the compiler. So ZAUTH005 is essentially a belt-and-suspenders check for forms the compiler couldn't catch (e.g. someone hand-edits the attribute usage in a way that slips through). Implement defensively:

  In `RequireSymbolWalker`, before adding to results, verify `type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct`. If not, emit ZAUTH005.

- Tests: not practically reachable from C# source (compiler rejects first), so test by passing a synthetic `INamedTypeSymbol` with `TypeKind.Interface` carrying a `[RequirePolicy]` attribute. Simpler: write a "ZAUTH005 does not fire on valid usage" negative test only, document that positive case requires synthesizing IL.
- Commit: `feat: emit ZAUTH005 for [RequirePolicy] on non-class/struct types (defensive)`

After Tasks 9-13, the generator has all five diagnostics. Run the full generator test suite to confirm green:

```powershell
dotnet test tests/ZeroAlloc.Authorization.Generator.Tests/ -c Release
```

---

### Task 14: AOT smoke scenario for new `[RequirePolicy]` flow

**Files:**
- Modify: `src/ZeroAlloc.Authorization.AotSmoke/Program.cs` (add a new scenario alongside existing)
- Modify: `src/ZeroAlloc.Authorization.AotSmoke/ZeroAlloc.Authorization.AotSmoke.csproj` (reference Microsoft.Extensions.DependencyInjection if not already referenced)

(Locate the existing AOT smoke binary first — it was shipped under Authorization #6. Likely at `samples/ZeroAlloc.Authorization.AotSmoke/` or `src/ZeroAlloc.Authorization.AotSmoke/`. Glob for it.)

**Step 1: Locate the AOT smoke project.**

```powershell
Get-ChildItem -Recurse -Filter "*.AotSmoke.csproj" | Select-Object FullName
```

**Step 2: Read its existing `Program.cs` to understand the scenario pattern.** Look for `GC.GetAllocatedBytesForCurrentThread` or similar allocation-gate checks.

**Step 3: Add a new scenario.**

Append (or insert before the existing assertions) a `[RequirePolicy]`-driven scenario:

```csharp
// New scenario: [RequirePolicy] dispatch through [Policy] evaluator
var services = new ServiceCollection();
services.AddZeroAllocAuthorization();  // generator-emitted
services.AddSingleton<ISecurityContext>(AnonymousSecurityContext.Instance);
using var sp = services.BuildServiceProvider();

using var scope = sp.CreateScope();
var ctx = scope.ServiceProvider.GetRequiredService<ISecurityContext>();
var authorizer = scope.ServiceProvider.GetRequiredService<AuthorizerFor<AotSmokeRequest>>();

// Warm-up call (JIT, DI cache populate)
_ = authorizer.EvaluateAsync(ctx).AsTask().GetAwaiter().GetResult();

// Measured call
var before = GC.GetAllocatedBytesForCurrentThread();
var result = authorizer.EvaluateAsync(ctx).AsTask().GetAwaiter().GetResult();
var after = GC.GetAllocatedBytesForCurrentThread();
var allocated = after - before;

Console.WriteLine($"[RequirePolicy AotSmoke] allocated={allocated}B success={result.IsSuccess}");
if (allocated > 0)
{
    Console.Error.WriteLine($"[FAIL] AuthorizerFor<T> happy path allocated {allocated}B; budget is 0");
    return 1;
}

// Plus declarations near the top of the file:
[Policy("aot")]
internal sealed class AotPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(UnitResult.Success<AuthorizationFailure>());
}

[RequirePolicy("aot")]
internal sealed record AotSmokeRequest;
```

**Step 4: Build + publish AOT + run.**

```powershell
dotnet publish src/ZeroAlloc.Authorization.AotSmoke/ -c Release -r win-x64 -p:PublishAot=true
$exe = Get-ChildItem src/ZeroAlloc.Authorization.AotSmoke/bin/Release/net10.0/win-x64/publish/*.exe | Select-Object -First 1
& $exe.FullName
```

Expected: prints `[RequirePolicy AotSmoke] allocated=0B success=True` and exits 0.

**Step 5: Commit.**

```powershell
git add src/ZeroAlloc.Authorization.AotSmoke/
git commit -m "test: AOT smoke scenario for [RequirePolicy] zero-alloc happy path"
```

---

### Task 15: **Breaking** — rewrite `IAuthorizationPolicy` to async-only, update affected v1 tests

**Files:**
- Modify: `src/ZeroAlloc.Authorization/IAuthorizationPolicy.cs` (collapse 4 methods to 1)
- Modify: any v1 tests that implement the old interface (find via grep)
- Modify: `src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt` (remove old method entries)

**Step 1: Identify affected sites.**

```powershell
Grep "IAuthorizationPolicy" c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization/tests/ -r
```

Every test class implementing the interface needs updating. Catalog them.

**Step 2: Rewrite the interface.**

Replace the contents of `src/ZeroAlloc.Authorization/IAuthorizationPolicy.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization;

/// <summary>
/// An authorization policy — given a security context, evaluates whether the
/// caller is authorized. Implementations are typically scoped DI services.
/// Sync-completing policies return <c>new ValueTask&lt;...&gt;(syncResult)</c>
/// — allocation-free on the stack.
/// </summary>
public interface IAuthorizationPolicy
{
    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default);
}
```

**Step 3: Update every test policy implementation.**

For each implementation found in Step 1, replace the four old methods with the single new one. Sync-style example:

```csharp
// before:
public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");

// after:
public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
    ISecurityContext ctx, CancellationToken ct = default)
    => new(ctx.Roles.Contains("Admin")
        ? UnitResult.Success<AuthorizationFailure>()
        : UnitResult.Failure(new AuthorizationFailure("policy.deny", "Admin role required")));
```

**Step 4: Update PublicAPI tracking.**

Move the old method lines from `PublicAPI.Shipped.txt` to a "removed" annotation by deleting them from Shipped and noting in Unshipped that they're removed (the PublicAPI analyzer enforces this; if it doesn't recognise removals, use `*REMOVED*` lines per its convention). The new `EvaluateAsync` signature already exists in Shipped — leave that.

**Step 5: Build + test.**

```powershell
dotnet build -c Release
dotnet test -c Release
```

Expected: 0 errors, all tests pass.

**Step 6: Commit.**

```powershell
git add src/ZeroAlloc.Authorization/IAuthorizationPolicy.cs src/ZeroAlloc.Authorization/PublicAPI.*.txt tests/
git commit -m "feat!: simplify IAuthorizationPolicy to single EvaluateAsync method

BREAKING CHANGE: drop sync IsAuthorized, async IsAuthorizedAsync, sync
Evaluate from IAuthorizationPolicy. All policies must now implement
async EvaluateAsync only. Sync-completing policies wrap their result
in new ValueTask<...>(result) — allocation-free."
```

---

### Task 16: **Breaking** — delete old `[AuthorizationPolicy]` and `[Authorize]`

**Files:**
- Delete: `src/ZeroAlloc.Authorization/AuthorizationPolicyAttribute.cs`
- Delete: `src/ZeroAlloc.Authorization/AuthorizeAttribute.cs`
- Modify: any source/test using these (find via grep) — they should all already use the v2 attributes from earlier tasks except possibly the v1-readiness tests
- Modify: `src/ZeroAlloc.Authorization/PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`

**Step 1: Find remaining usages.**

```powershell
Grep "AuthorizationPolicyAttribute|AuthorizeAttribute|\[AuthorizationPolicy|\[Authorize\(" c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization/ -r
```

**Step 2: Update each site.**

`[AuthorizationPolicy("foo")]` → `[Policy("foo")]`. `[Authorize("foo")]` → `[RequirePolicy("foo")]`.

**Step 3: Delete the v1 attribute files.**

```powershell
Remove-Item src/ZeroAlloc.Authorization/AuthorizationPolicyAttribute.cs
Remove-Item src/ZeroAlloc.Authorization/AuthorizeAttribute.cs
```

**Step 4: Update PublicAPI tracking — same removal pattern as Task 15.**

**Step 5: Build + test.**

```powershell
dotnet build -c Release
dotnet test -c Release
```

Expected: 0 errors, all tests pass.

**Step 6: Commit.**

```powershell
git add -A src/ src/ZeroAlloc.Authorization/PublicAPI.*.txt tests/
git commit -m "feat!: remove [AuthorizationPolicy] and [Authorize] attributes

BREAKING CHANGE: [AuthorizationPolicy] renamed to [Policy].
[Authorize] renamed to [RequirePolicy]. The rename eliminates name
collision with Microsoft.AspNetCore.Authorization.AuthorizeAttribute
and pairs cleanly with [Policy] (declare-side / require-side
grammatical pair)."
```

---

### Task 17: Update `PublicAPI.Shipped.txt` + release-please prep

**Files:**
- Modify: `src/ZeroAlloc.Authorization/PublicAPI.Shipped.txt`
- Modify: `src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt` (clear)

**Step 1: Move Unshipped → Shipped.**

The `PublicAPI.Unshipped.txt` currently contains the v2 additions (Policy, RequirePolicy, AuthorizerFor) and the v1 removals. Move them into Shipped.txt and clear Unshipped:

```powershell
# Read both, append unshipped to shipped, clear unshipped
$shipped = "src/ZeroAlloc.Authorization/PublicAPI.Shipped.txt"
$unshipped = "src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt"
$existingShipped = Get-Content $shipped
$existingUnshipped = Get-Content $unshipped
# Manually consolidate — the v2 shape should match what was approved in the design doc.
# Specifically: remove the [AuthorizationPolicy] + [Authorize] lines from Shipped,
# remove the four old IAuthorizationPolicy method lines from Shipped,
# add the v2 additions.
```

Edit `PublicAPI.Shipped.txt` to be exactly the v2 surface:

```
#nullable enable
ZeroAlloc.Authorization.AnonymousSecurityContext
ZeroAlloc.Authorization.AnonymousSecurityContext.Claims.get -> System.Collections.Generic.IReadOnlyDictionary<string!, string!>!
ZeroAlloc.Authorization.AnonymousSecurityContext.Id.get -> string!
ZeroAlloc.Authorization.AnonymousSecurityContext.Roles.get -> System.Collections.Generic.IReadOnlySet<string!>!
ZeroAlloc.Authorization.AuthorizationFailure
ZeroAlloc.Authorization.AuthorizationFailure.AuthorizationFailure() -> void
ZeroAlloc.Authorization.AuthorizationFailure.AuthorizationFailure(string! code, string? reason = null) -> void
ZeroAlloc.Authorization.AuthorizationFailure.Code.get -> string!
ZeroAlloc.Authorization.AuthorizationFailure.Reason.get -> string?
ZeroAlloc.Authorization.AuthorizerFor<TRequest>
ZeroAlloc.Authorization.AuthorizerFor<TRequest>.AuthorizerFor() -> void
ZeroAlloc.Authorization.IAuthorizationPolicy
ZeroAlloc.Authorization.IAuthorizationPolicy.EvaluateAsync(ZeroAlloc.Authorization.ISecurityContext! ctx, System.Threading.CancellationToken ct = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.UnitResult<ZeroAlloc.Authorization.AuthorizationFailure>>
ZeroAlloc.Authorization.ISecurityContext
ZeroAlloc.Authorization.ISecurityContext.Claims.get -> System.Collections.Generic.IReadOnlyDictionary<string!, string!>!
ZeroAlloc.Authorization.ISecurityContext.Id.get -> string!
ZeroAlloc.Authorization.ISecurityContext.Roles.get -> System.Collections.Generic.IReadOnlySet<string!>!
ZeroAlloc.Authorization.PolicyAttribute
ZeroAlloc.Authorization.PolicyAttribute.Name.get -> string!
ZeroAlloc.Authorization.PolicyAttribute.PolicyAttribute(string! name) -> void
ZeroAlloc.Authorization.RequirePolicyAttribute
ZeroAlloc.Authorization.RequirePolicyAttribute.PolicyName.get -> string!
ZeroAlloc.Authorization.RequirePolicyAttribute.RequirePolicyAttribute(string! policyName) -> void
abstract ZeroAlloc.Authorization.AuthorizerFor<TRequest>.EvaluateAsync(ZeroAlloc.Authorization.ISecurityContext! ctx, System.Threading.CancellationToken ct = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.UnitResult<ZeroAlloc.Authorization.AuthorizationFailure>>
const ZeroAlloc.Authorization.AnonymousSecurityContext.AnonymousId = "anonymous" -> string!
const ZeroAlloc.Authorization.AuthorizationFailure.DefaultDenyCode = "policy.deny" -> string!
static readonly ZeroAlloc.Authorization.AnonymousSecurityContext.Instance -> ZeroAlloc.Authorization.AnonymousSecurityContext!
```

Clear `PublicAPI.Unshipped.txt`:

```
#nullable enable
```

**Step 2: Build to verify PublicAPI analyzer is satisfied.**

```powershell
dotnet build -c Release
```

Expected: 0 errors, in particular no RS0016 (missing) or RS0017 (extra) warnings.

**Step 3: Commit.**

```powershell
git add src/ZeroAlloc.Authorization/PublicAPI.Shipped.txt src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt
git commit -m "chore: promote v2 PublicAPI to Shipped, clear Unshipped"
```

---

### Task 18: End-to-end verification + push + open PR

**Step 1: Revert `global.json` if it was edited.**

```powershell
Get-Content c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization/global.json
```

If it shows `10.0.204`, edit it back to `10.0.300`. `git status` should show no `global.json` modification after revert (since 10.0.300 is what's in HEAD).

**Step 2: Confirm clean tree + branch diff.**

```powershell
git status --short
git log --oneline main..HEAD
git diff main..HEAD --stat
```

Expected commits (subjects, hashes will differ):
1. `docs(plans): policy registry generator design (v2)` — already committed at task 0
2. `feat: add [Policy] and [RequirePolicy] attributes`
3. `feat: add AuthorizerFor<TRequest> abstract base`
4. `build: scaffold ZeroAlloc.Authorization.Generator analyzer project`
5. `feat: add policy and require-policy symbol walkers`
6. `feat: emit AuthorizerFor<T> subclass per [RequirePolicy]-decorated type`
7. `feat: emit AddZeroAllocAuthorization DI registration extension`
8. `feat: wire PolicyRegistryGenerator pipeline + first snapshot test`
9. `feat: cross-assembly policy discovery`
10-14. Five diagnostics commits (`feat: emit ZAUTH001..005 ...`)
15. `test: AOT smoke scenario for [RequirePolicy] zero-alloc happy path`
16. `feat!: simplify IAuthorizationPolicy to single EvaluateAsync method`
17. `feat!: remove [AuthorizationPolicy] and [Authorize] attributes`
18. `chore: promote v2 PublicAPI to Shipped, clear Unshipped`

Confirm `global.json` is NOT in the diff.

**Step 3: Full clean rebuild + test.**

```powershell
dotnet clean -c Release
dotnet build -c Release
dotnet test -c Release
```

Expected: full green. Existing v1 contract tests + new v2 attribute tests + new generator snapshot tests + new diagnostic tests + AOT smoke.

**Step 4: Push.**

```powershell
git push -u origin feat/policy-registry-generator-v2
```

**Step 5: Open PR.**

```powershell
$body = @'
## Summary
ZA.Authorization v2 — source-generated policy registry. Lifts the `~80 LOC` generator + `~70 LOC` runtime plumbing currently in Mediator.Authorization into the contract repo. Renames `[AuthorizationPolicy]` → `[Policy]` and `[Authorize]` → `[RequirePolicy]` to eliminate name collision with `Microsoft.AspNetCore.Authorization.AuthorizeAttribute`. Simplifies `IAuthorizationPolicy` to a single async `EvaluateAsync` method.

The Mediator.Authorization v5 release that consumes this generator is a follow-on PR in a separate repo.

## Why
Graduation signal fired by Mediator PR #74 (2026-05-06): the host-side generator now lives in the wrong repo. Lifting it consolidates the duplication and aligns Authorization with the proven `Mediator.Validation` DI generic dispatch pattern (`AuthorizerFor<TRequest>`).

## Breaking changes (v1.2.2 → v2.0.0)
- `[AuthorizationPolicy("admin")]` → `[Policy("admin")]`
- `[Authorize("admin")]` → `[RequirePolicy("admin")]`
- `IAuthorizationPolicy` reduced to single `EvaluateAsync(ISecurityContext, CancellationToken)`
- Method-level `[RequirePolicy]` rejected (`ZAUTH005`) — class-level only
- v1 helpers (`Resolve(string)`, `GetPoliciesFor<T>()`) — never existed in ZA.Authorization, they were in Mediator.Authorization. No impact on this repo.

## Test plan
- [x] `dotnet test` green: existing v1 contract tests + new attribute tests + 2 generator snapshot tests + 10 diagnostic tests (positive + negative for each of ZAUTH001-005)
- [x] AOT smoke binary: new `[RequirePolicy]` scenario asserts 0 B allocated on happy-path Evaluate
- [x] Cross-assembly test: policy in LibA, `[RequirePolicy]` in consumer referencing LibA — discovered correctly
- [x] `PublicAPI.Shipped.txt` diff intentional (v2 surface)
- [x] `dotnet pack` produces `ZeroAlloc.Authorization.2.0.0.nupkg` with `analyzers/dotnet/cs/ZeroAlloc.Authorization.Generator.dll` bundled (no separate Generator package)

## Follow-on
- Mediator.Authorization v5.0.0 (separate PR in `ZeroAlloc.Mediator` repo) consumes this generator; deletes `~80 LOC` generator + `~70 LOC` runtime hooks; replaces `AuthorizationBehavior` with `~20 LOC` DI-resolved version.

## Design reference
[docs/plans/2026-05-19-policy-registry-generator-design.md](docs/plans/2026-05-19-policy-registry-generator-design.md)
'@
gh pr create --repo ZeroAlloc-Net/ZeroAlloc.Authorization --title "feat!: v2 — source-generated policy registry + [Policy]/[RequirePolicy] rename" --body $body --base main
```

Expected: PR URL returned.

**Step 6: Watch CI.**

```powershell
gh pr checks <PR#> --repo ZeroAlloc-Net/ZeroAlloc.Authorization
```

If anything fails, pull the failing job's log and investigate. The most likely failures and their fixes:
- **SDK version mismatch on CI** — CI has 10.0.300+, local rolls forward. Should not fire.
- **PublicAPI analyzer (RS0016/RS0017)** — if a public type was added/removed without updating Shipped.txt. Re-read Task 17.
- **AOT smoke alloc gate** — if the new scenario allocates >0 bytes, inspect the generated code for hidden boxing (`object` casts on value-typed `UnitResult<T>`).

---

## Notes for the executor

- **DRY:** five diagnostics share a single `Descriptors.cs`. Don't duplicate descriptor definitions per file.
- **YAGNI:** no warning diagnostics in v1 (orphan-policy detection was considered and rejected — false-positive surface). Don't add suggestions, code fixes, or analyzer-only modes.
- **TDD:** every task that produces runtime code starts with a failing test. Generator tasks use snapshot tests; diagnostic tasks use Roslyn diagnostic assertions.
- **Frequent commits:** 18 commits, one per task. Conventional-commit prefixes (`feat:`, `feat!:`, `fix:`, `test:`, `docs:`, `chore:`) drive release-please's v2.0.0 bump on merge.
- **Subagent dispatch hint:** the diagnostic tasks (9-13) are mechanically similar; consider dispatching them with a template prompt that varies only by code/name. The other tasks are sequential and benefit from review-between-tasks.
