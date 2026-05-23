using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Authorization.Generator.Tests;

public sealed class RequireAnyPolicyGeneratorTests
{
    [Fact]
    public Task OrGroup_TwoCandidates_GeneratesShortCircuitEval()
    {
        const string source = @"
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;

namespace MyApp;

[Policy(""Premium"")]
public sealed class PremiumPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(UnitResult<AuthorizationFailure>.Success());
}

[Policy(""Trusted"")]
public sealed class TrustedPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(UnitResult<AuthorizationFailure>.Success());
}

[RequireAnyPolicy(""Premium"", ""Trusted"")]
public sealed record ViewBillingQuery();
";
        return RunAndVerify(source);
    }

    [Fact]
    public Task MixedAndOr_AdminPlusAnyOfPremiumOrTrusted()
    {
        const string source = @"
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;

namespace MyApp;

[Policy(""Admin"")]
public sealed class AdminPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(UnitResult<AuthorizationFailure>.Success());
}

[Policy(""Premium"")]
public sealed class PremiumPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(UnitResult<AuthorizationFailure>.Success());
}

[Policy(""Trusted"")]
public sealed class TrustedPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(UnitResult<AuthorizationFailure>.Success());
}

[RequirePolicy(""Admin"")]
[RequireAnyPolicy(""Premium"", ""Trusted"")]
public sealed record ViewBillingQuery();
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
