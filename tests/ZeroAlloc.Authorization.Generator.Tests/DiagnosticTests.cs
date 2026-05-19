using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Authorization.Generator.Tests;

public sealed class DiagnosticTests
{
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
    public void ZAUTH002_DuplicatePolicyNames_FiresError()
    {
        var source = @"
using ZeroAlloc.Authorization;
namespace MyApp;

[Policy(""admin"")]
public sealed class AdminPolicyA : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => true;
}

[Policy(""admin"")]
public sealed class AdminPolicyB : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => true;
}
";
        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "ZAUTH002" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ZAUTH002_UniquePolicyNames_DoesNotFire()
    {
        var source = @"
using ZeroAlloc.Authorization;
namespace MyApp;

[Policy(""admin"")]
public sealed class AdminPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => true;
}

[Policy(""user"")]
public sealed class UserPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => true;
}
";
        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "ZAUTH002");
    }

    [Fact]
    public void ZAUTH001_KnownPolicy_DoesNotFire()
    {
        var source = @"
using ZeroAlloc.Authorization;
namespace MyApp;

[Policy(""admin"")]
public sealed class AdminPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => true;
}

[RequirePolicy(""admin"")]
public sealed record Foo(int Id);
";
        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "ZAUTH001");
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
