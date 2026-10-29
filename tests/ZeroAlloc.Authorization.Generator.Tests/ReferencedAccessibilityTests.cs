using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Authorization.Generator.Tests;

/// <summary>
/// The generator runs in every project above a library that declares policies or requests. The
/// generated code names those types, so a type the project cannot access must not be collected:
/// it would not compile in the project's own generated file.
/// </summary>
public sealed class ReferencedAccessibilityTests
{
    private const string Usings = """
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Authorization;
        using ZeroAlloc.Results;

        """;

    private const string Admin = """
        [Policy("admin")]
        public sealed class AdminPolicy : IAuthorizationPolicy
        {
            public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, CancellationToken ct = default)
                => new(UnitResult<AuthorizationFailure>.Success());
        }

        """;

    private const string Hidden = """
        [Policy("hidden")]
        internal sealed class HiddenPolicy : IAuthorizationPolicy
        {
            public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, CancellationToken ct = default)
                => new(UnitResult<AuthorizationFailure>.Success());
        }

        """;

    private sealed record Layer(Compilation Output, string Generated, IReadOnlyList<Diagnostic> Errors, IReadOnlyList<Diagnostic> All);

    private static Layer Build(string name, string source, params Layer[] references)
    {
        var refs = StandardReferences().Concat(references.Select(r => r.Output.ToMetadataReference())).ToList();
        var compilation = CSharpCompilation.Create(
            name,
            new[] { CSharpSyntaxTree.ParseText(Usings + source) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(new[] { new PolicyRegistryGenerator().AsSourceGenerator() })
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        var generated = string.Concat(driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
        var all = output.GetDiagnostics().Concat(generatorDiagnostics).ToList();
        return new Layer(output, generated, all.Where(d => d.Severity == DiagnosticSeverity.Error).ToList(), all);
    }

    // The library is built without the generator, as it would be if it only declares the types.
    private static Layer Library(string source) => BuildWithoutGenerator("Lib", source);

    private static Layer BuildWithoutGenerator(string name, string source)
    {
        var compilation = CSharpCompilation.Create(
            name,
            new[] { CSharpSyntaxTree.ParseText(Usings + source) },
            StandardReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return new Layer(compilation, string.Empty, new List<Diagnostic>(), new List<Diagnostic>());
    }

    private const string AppSource = "namespace App; public sealed class Nothing { }";

    [Fact]
    public void InternalPolicyAndInternalRequest_InReferencedLibrary_AreNotCollected()
    {
        var lib = Library(Hidden + """
            [RequirePolicy("hidden")]
            internal sealed record HiddenRequest(int Id);
            """);

        var app = Build("App", AppSource, lib);

        Assert.Empty(app.Errors);
        Assert.DoesNotContain("HiddenPolicy", app.Generated);
        Assert.DoesNotContain("HiddenRequest", app.Generated);
    }

    [Fact]
    public void InternalRequest_RequiringPublicPolicy_IsNotCollected()
    {
        var lib = Library(Admin + """
            [RequirePolicy("admin")]
            internal sealed record HiddenRequest(int Id);
            """);

        var app = Build("App", AppSource, lib);

        Assert.Empty(app.Errors);
        Assert.DoesNotContain("HiddenRequest", app.Generated);
        Assert.Contains("AddScoped<global::AdminPolicy>", app.Generated);
    }

    [Fact]
    public void PublicRequest_RequiringInternalPolicy_IsLeftOutWithoutDiagnostic()
    {
        var lib = Library(Hidden + """
            [RequirePolicy("hidden")]
            public sealed record PublicRequest(int Id);
            """);

        var app = Build("App", AppSource, lib);

        Assert.Empty(app.Errors);
        Assert.DoesNotContain(app.All, d => d.Id == "ZAUTH001");
        Assert.DoesNotContain("PublicRequest", app.Generated);
    }

    [Fact]
    public void PublicTypes_InReferencedLibrary_AreStillCollected()
    {
        var lib = Library(Admin + """
            [RequirePolicy("admin")]
            public sealed record PublicRequest(int Id);
            """);

        var app = Build("App", AppSource, lib);

        Assert.Empty(app.Errors);
        Assert.Contains("AddScoped<global::AdminPolicy>", app.Generated);
        Assert.Contains("AuthorizerFor<global::PublicRequest>", app.Generated);
    }

    [Fact]
    public void PublicNestedTypes_InsideInternalContainer_AreNotCollected()
    {
        var lib = Library("""
            internal static class Container
            {
                [Policy("nested")]
                public sealed class NestedPolicy : IAuthorizationPolicy
                {
                    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, CancellationToken ct = default)
                        => new(UnitResult<AuthorizationFailure>.Success());
                }

                [RequirePolicy("nested")]
                public sealed record NestedRequest(int Id);
            }
            """);

        var app = Build("App", AppSource, lib);

        Assert.Empty(app.Errors);
        Assert.DoesNotContain("NestedPolicy", app.Generated);
        Assert.DoesNotContain("NestedRequest", app.Generated);
    }

    [Fact]
    public void PublicPolicy_WithInternalTypeArgument_IsNotCollected()
    {
        var lib = Library("""
            internal sealed record Secret(int Id);

            [Policy("typed")]
            public sealed class TypedPolicy : IAuthorizationPolicy<Secret>
            {
                public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, Secret a, CancellationToken ct = default)
                    => new(UnitResult<AuthorizationFailure>.Success());
            }
            """);

        var app = Build("App", AppSource, lib);

        Assert.Empty(app.Errors);
        Assert.DoesNotContain("TypedPolicy", app.Generated);
    }

    [Fact]
    public void InternalTypes_WithInternalsVisibleToTheProject_AreCollected()
    {
        var lib = Library("""
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("App")]

            """ + Hidden + """
            [RequirePolicy("hidden")]
            internal sealed record HiddenRequest(int Id);
            """);

        var app = Build("App", AppSource, lib);

        Assert.Empty(app.Errors);
        Assert.Contains("AddScoped<global::HiddenPolicy>", app.Generated);
        Assert.Contains("AuthorizerFor<global::HiddenRequest>", app.Generated);
    }

    [Fact]
    public void InternalTypes_WithInternalsVisibleToAnotherProject_AreNotCollected()
    {
        var lib = Library("""
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SomethingElse")]

            """ + Hidden);

        var app = Build("App", AppSource, lib);

        Assert.Empty(app.Errors);
        Assert.DoesNotContain("HiddenPolicy", app.Generated);
    }

    private static List<MetadataReference> StandardReferences() =>
        ((string)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(System.IO.Path.PathSeparator, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
}
