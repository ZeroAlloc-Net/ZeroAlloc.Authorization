using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Authorization.Generator.Tests;

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
        => new(UnitResult<AuthorizationFailure>.Success());
}

[RequirePolicy(""admin"")]
public sealed record DeleteUser(int Id);
";

    [Fact]
    public void Basic_SinglePolicy_SingleRequirePolicy_Snapshot()
    {
        RunAndVerify(BasicSource);
    }

    [Fact]
    public void EmptyCompilation_StillEmitsRegistrationWithMarker_Snapshot()
    {
        RunAndVerify("namespace MyApp; public sealed class Nothing { }");
    }

    [Fact]
    public void EmptyCompilation_EmitsCompilableRegistration()
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText("namespace MyApp; public sealed class Nothing { }") },
            GetStandardReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new PolicyRegistryGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(driver.GetRunResult().GeneratedTrees).ToString();
        Assert.Contains("TryAddSingleton<global::ZeroAlloc.Authorization.ZeroAllocAuthorizationMarker>", generated);
    }

    [Fact]
    public void EmptyCompilation_WithoutDependencyInjection_EmitsNothing()
    {
        var references = GetStandardReferences()
            .Where(r => !(r.Display ?? "").Contains("Microsoft.Extensions.DependencyInjection"))
            .ToList();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText("namespace MyApp; public sealed class Nothing { }") },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new PolicyRegistryGenerator().AsSourceGenerator())
            .RunGenerators(compilation);

        Assert.Empty(driver.GetRunResult().GeneratedTrees);
    }

    [Fact]
    public void CrossAssembly_PolicyInReferencedAsm_RequireInSource_Snapshot()
    {
        // Build a "SharedKernel" assembly that defines the policy
        const string libASource = @"
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
        => new(UnitResult<AuthorizationFailure>.Success());
}
";
        var standardRefs = GetStandardReferences();
        var libACompilation = CSharpCompilation.Create(
            "SharedKernel",
            new[] { CSharpSyntaxTree.ParseText(libASource) },
            standardRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var libAStream = new System.IO.MemoryStream();
        var emitResult = libACompilation.Emit(libAStream);
        Assert.True(emitResult.Success, "SharedKernel emit failed");
        libAStream.Position = 0;
        var libARef = MetadataReference.CreateFromStream(libAStream);

        // Build consumer that references LibA and has [RequirePolicy] using its policy
        const string consumerSource = @"
using ZeroAlloc.Authorization;

namespace MyApp;

[RequirePolicy(""admin"")]
public sealed record DeleteUser(int Id);
";
        var allRefs = new List<MetadataReference>(standardRefs);
        allRefs.Add(libARef);

        var consumerCompilation = CSharpCompilation.Create(
            "Consumer",
            new[] { CSharpSyntaxTree.ParseText(consumerSource) },
            allRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new PolicyRegistryGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(consumerCompilation);
        GeneratorSnapshot.Verify(driver);
    }

    private static List<MetadataReference> GetStandardReferences()
    {
        // Force-load contract + runtime assemblies so they appear in the AppDomain, otherwise
        // the test compilation cannot semantic-bind [Policy] / [RequirePolicy] / IAuthorizationPolicy.
        _ = typeof(ZeroAlloc.Authorization.PolicyAttribute).FullName;
        _ = typeof(ZeroAlloc.Results.UnitResult<>).FullName;
        _ = typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).FullName;
        _ = typeof(Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions).FullName;

        var references = new List<MetadataReference>();
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            if (string.IsNullOrEmpty(asm.Location)) continue;
            references.Add(MetadataReference.CreateFromFile(asm.Location));
        }
        return references;
    }

    private static void RunAndVerify(string source)
    {
        var references = GetStandardReferences();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new PolicyRegistryGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        GeneratorSnapshot.Verify(driver);
    }
}
