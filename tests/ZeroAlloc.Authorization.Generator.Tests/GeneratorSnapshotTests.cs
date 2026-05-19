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

    private static Task RunAndVerify(string source)
    {
        // Force-load contract + runtime assemblies so they appear in the AppDomain, otherwise
        // the test compilation cannot semantic-bind [Policy] / [RequirePolicy] / IAuthorizationPolicy.
        _ = typeof(ZeroAlloc.Authorization.PolicyAttribute).FullName;
        _ = typeof(ZeroAlloc.Results.UnitResult<>).FullName;

        var references = new System.Collections.Generic.List<MetadataReference>();
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            if (string.IsNullOrEmpty(asm.Location)) continue;
            references.Add(MetadataReference.CreateFromFile(asm.Location));
        }

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
