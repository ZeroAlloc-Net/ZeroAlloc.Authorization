using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Authorization.Generator.Tests;

public sealed class ParameterizedPolicyGeneratorTests
{
    [Fact]
    public Task SingleIntArg_GeneratesTypedDispatch()
    {
        const string source = @"
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;

namespace MyApp;

[Policy(""MinAge"")]
public sealed class MinAgePolicy : IAuthorizationPolicy<int>
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, int arg1, CancellationToken ct = default)
        => new(UnitResult<AuthorizationFailure>.Success());
}

[RequirePolicy(""MinAge"", 18)]
public sealed record ApplyForLicenseCommand();
";
        return RunAndVerify(source);
    }

    [Fact]
    public Task TwoArgs_StringAndInt_GeneratesTypedDispatch()
    {
        const string source = @"
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;

namespace MyApp;

[Policy(""Permission"")]
public sealed class PermissionPolicy : IAuthorizationPolicy<string, int>
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, string arg1, int arg2, CancellationToken ct = default)
        => new(UnitResult<AuthorizationFailure>.Success());
}

[RequirePolicy(""Permission"", ""read"", 42)]
public sealed record ReadDocCommand();
";
        return RunAndVerify(source);
    }

    private static List<MetadataReference> GetStandardReferences()
    {
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
