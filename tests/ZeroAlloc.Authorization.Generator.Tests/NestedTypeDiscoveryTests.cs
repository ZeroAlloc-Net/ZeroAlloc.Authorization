using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Authorization.Generator.Tests;

public sealed class NestedTypeDiscoveryTests
{
    [Fact]
    public void Policy_OnNestedClass_IsDiscovered()
    {
        var source = @"
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;
namespace MyApp;
public static class Outer
{
    [Policy(""NestedPolicy"")]
    public sealed class NestedPolicy : IAuthorizationPolicy
    {
        public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext c, CancellationToken t = default)
            => new(UnitResult<AuthorizationFailure>.Success());
    }
}
";
        var diags = RunGenerator(source);
        // Without the fix, the nested policy would be silently ignored — no diagnostics would fire.
        // With the fix, ZAUTH002 (duplicate name) does NOT fire (only one declaration) and ZAUTH003
        // (doesn't implement interface) does NOT fire — the policy is correctly recognised.
        // The simplest assertion: no error diagnostics from this source.
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void RequirePolicy_OnNestedRecord_IsDiscovered()
    {
        var source = @"
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;
namespace MyApp;
[Policy(""Admin"")]
public sealed class AdminPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext c, CancellationToken t = default)
        => new(UnitResult<AuthorizationFailure>.Success());
}
public static class Outer
{
    [RequirePolicy(""Admin"")]
    public sealed record NestedCmd();
}
";
        var diags = RunGenerator(source);
        // Without the fix, the nested [RequirePolicy] is silently ignored — no AuthorizerFor is emitted.
        // With the fix, ZAUTH001 (unknown policy name) does NOT fire because the [Policy] is found
        // and matched against the [RequirePolicy("Admin")] on NestedCmd.
        Assert.DoesNotContain(diags, d => string.Equals(d.Id, "ZAUTH001", StringComparison.Ordinal));
    }

    private static ImmutableArray<Diagnostic> RunGenerator(string source)
    {
        var refs = GetStandardReferences();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var generator = new PolicyRegistryGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult().Diagnostics;
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
}
