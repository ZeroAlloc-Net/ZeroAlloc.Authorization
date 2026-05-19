using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
    public bool IsAuthorized(ISecurityContext ctx) => true;
}

[RequirePolicy(""admin"")]
public sealed record DeleteUser(int Id);
";

    [Fact]
    public Task Basic_SinglePolicy_SingleRequirePolicy_Snapshot()
    {
        return RunAndVerify(BasicSource);
    }

    [Fact]
    public async Task CrossAssembly_PolicyInReferencedAsm_RequireInSource_Snapshot()
    {
        // Build a "SharedKernel" assembly that defines the policy
        const string libASource = @"
using ZeroAlloc.Authorization;

namespace SharedKernel;

[Policy(""admin"")]
public sealed class AdminPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => true;
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
        await Verifier.Verify(driver).UseDirectory("Snapshots");
    }

    private static List<MetadataReference> GetStandardReferences()
    {
        // Force-load contract + runtime assemblies so they appear in the AppDomain, otherwise
        // the test compilation cannot semantic-bind [Policy] / [RequirePolicy] / IAuthorizationPolicy.
        _ = typeof(ZeroAlloc.Authorization.PolicyAttribute).FullName;
        _ = typeof(ZeroAlloc.Results.UnitResult<>).FullName;

        var references = new List<MetadataReference>();
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            if (string.IsNullOrEmpty(asm.Location)) continue;
            references.Add(MetadataReference.CreateFromFile(asm.Location));
        }
        return references;
    }

    private static Task RunAndVerify(string source)
    {
        var references = GetStandardReferences();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new PolicyRegistryGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return Verifier.Verify(driver).UseDirectory("Snapshots");
    }
}
