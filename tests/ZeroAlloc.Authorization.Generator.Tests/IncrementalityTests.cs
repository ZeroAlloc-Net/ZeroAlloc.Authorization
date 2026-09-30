using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Authorization.Generator.Tests;

/// <summary>
/// The generator's work on an edit is bounded by what the edit touches. An edit to a file with no
/// [Policy] or [Require...] type, or a new unrelated file, leaves every tracked step and the
/// output cached, and the referenced assemblies are only scanned again when the references change.
/// </summary>
public sealed class IncrementalityTests
{
    // The pipeline's tracking names, as PolicyRegistryGenerator gives them.
    private static readonly string[] TrackedSteps =
    [
        "SourcePolicies",
        "SourceRequirePolicies",
        "SourceRequireAnyPolicies",
        "ReferenceScan",
        "EmitInputs",
        "DiagnosticInputs",
    ];

    private const string PolicyFile = """
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Authorization;
        using ZeroAlloc.Results;
        namespace MyApp;

        [Policy("admin")]
        public sealed class AdminPolicy : IAuthorizationPolicy
        {
            public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, CancellationToken ct = default)
                => new(UnitResult<AuthorizationFailure>.Success());
        }

        [Policy("MinAge")]
        public sealed class MinAgePolicy : IAuthorizationPolicy<int>
        {
            public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, int a, CancellationToken ct = default)
                => new(UnitResult<AuthorizationFailure>.Success());
        }

        [Policy("broken")]
        public sealed class BrokenPolicy { }
        """;

    private const string RequestFile = """
        using ZeroAlloc.Authorization;
        namespace MyApp;

        [RequirePolicy("admin")]
        [RequireAnyPolicy("admin", "missing")]
        public sealed record DeleteUser(int Id);

        [RequirePolicy("MinAge", 18)]
        public sealed record BuyBeer(int Id);
        """;

    private const string UnrelatedFile = """
        namespace MyApp;

        public static class Unrelated
        {
            public static int Answer() => 42;
        }
        """;

    [Fact]
    public void UnrelatedEdit_LeavesEveryTrackedStepAndTheOutputCached()
    {
        var unrelated = CSharpSyntaxTree.ParseText(UnrelatedFile, path: "/src/Unrelated.cs");
        var compilation = CreateCompilation(
            CSharpSyntaxTree.ParseText(PolicyFile, path: "/src/Policies.cs"),
            CSharpSyntaxTree.ParseText(RequestFile, path: "/src/Requests.cs"),
            unrelated);

        var driver = CreateDriver().RunGenerators(compilation);
        var first = driver.GetRunResult();

        var edited = unrelated.WithChangedText(SourceText.From(
            UnrelatedFile.Replace("42", "43", StringComparison.Ordinal)));
        var next = compilation
            .ReplaceSyntaxTree(unrelated, edited)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace MyApp; public sealed class Added { }", path: "/src/Added.cs"));
        var second = driver.RunGenerators(next).GetRunResult();

        Assert.Equal(Generated(first), Generated(second));
        Assert.Equal(Describe(first.Diagnostics), Describe(second.Diagnostics));

        var steps = second.Results[0].TrackedSteps;
        foreach (var name in TrackedSteps)
        {
            Assert.True(steps.ContainsKey(name), $"step {name} is not tracked");
            foreach (var step in steps[name])
            {
                foreach (var (_, reason) in step.Outputs)
                {
                    Assert.True(
                        reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                        $"step {name} was {reason}");
                }
            }
        }

        foreach (var (_, runs) in second.Results[0].TrackedOutputSteps)
        {
            foreach (var run in runs)
            {
                foreach (var (_, reason) in run.Outputs)
                {
                    Assert.Equal(IncrementalStepRunReason.Cached, reason);
                }
            }
        }
    }

    [Fact]
    public void EditToAPolicyFile_ReachesTheOutput_ButDoesNotScanTheReferencesAgain()
    {
        var policies = CSharpSyntaxTree.ParseText(PolicyFile, path: "/src/Policies.cs");
        var compilation = CreateCompilation(
            policies,
            CSharpSyntaxTree.ParseText(RequestFile, path: "/src/Requests.cs"));

        var driver = CreateDriver().RunGenerators(compilation);

        var edited = policies.WithChangedText(SourceText.From(
            PolicyFile.Replace(
                "public sealed class BrokenPolicy { }",
                """
                public sealed class BrokenPolicy : IAuthorizationPolicy
                {
                    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, CancellationToken ct = default)
                        => new(UnitResult<AuthorizationFailure>.Success());
                }
                """,
                StringComparison.Ordinal)));
        var result = driver.RunGenerators(compilation.ReplaceSyntaxTree(policies, edited)).GetRunResult();

        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZAUTH003", StringComparison.Ordinal));
        Assert.Contains("AddScoped<global::MyApp.BrokenPolicy>", Generated(result), StringComparison.Ordinal);
        var referenceScan = Assert.Single(result.Results[0].TrackedSteps["ReferenceScan"]);
        Assert.All(referenceScan.Outputs, o => Assert.Equal(IncrementalStepRunReason.Cached, o.Reason));
    }

    [Fact]
    public void MovingAPolicy_RerunsTheDiagnostics_ButNotTheEmittedSource()
    {
        var policies = CSharpSyntaxTree.ParseText(PolicyFile, path: "/src/Policies.cs");
        var compilation = CreateCompilation(
            policies,
            CSharpSyntaxTree.ParseText(RequestFile, path: "/src/Requests.cs"));

        var driver = CreateDriver().RunGenerators(compilation);
        var first = driver.GetRunResult();

        var moved = policies.WithChangedText(SourceText.From(
            PolicyFile.Replace("namespace MyApp;", "namespace MyApp;\n\n// two\n// more lines", StringComparison.Ordinal)));
        var second = driver.RunGenerators(compilation.ReplaceSyntaxTree(policies, moved)).GetRunResult();

        Assert.Equal(Generated(first), Generated(second));
        var steps = second.Results[0].TrackedSteps;
        Assert.All(Assert.Single(steps["EmitInputs"]).Outputs, o => Assert.Equal(IncrementalStepRunReason.Unchanged, o.Reason));
        Assert.All(Assert.Single(steps["DiagnosticInputs"]).Outputs, o => Assert.Equal(IncrementalStepRunReason.Modified, o.Reason));

        var before = Assert.Single(first.Diagnostics, d => string.Equals(d.Id, "ZAUTH003", StringComparison.Ordinal));
        var after = Assert.Single(second.Diagnostics, d => string.Equals(d.Id, "ZAUTH003", StringComparison.Ordinal));
        Assert.Same(moved, after.Location.SourceTree);
        Assert.Equal(
            before.Location.GetLineSpan().StartLinePosition.Line + 3,
            after.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void NewReference_ScansTheReferencesAgain()
    {
        var compilation = CreateCompilation(CSharpSyntaxTree.ParseText(RequestFile, path: "/src/Requests.cs"));
        var driver = CreateDriver().RunGenerators(compilation);
        Assert.Contains(driver.GetRunResult().Diagnostics, d => string.Equals(d.Id, "ZAUTH001", StringComparison.Ordinal));

        var library = CSharpCompilation.Create(
            "Library",
            [CSharpSyntaxTree.ParseText(PolicyFile, path: "/lib/Policies.cs")],
            StandardReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var result = driver.RunGenerators(compilation.AddReferences(library.ToMetadataReference())).GetRunResult();

        var referenceScan = Assert.Single(result.Results[0].TrackedSteps["ReferenceScan"]);
        Assert.All(referenceScan.Outputs, o => Assert.Equal(IncrementalStepRunReason.Modified, o.Reason));
        Assert.Contains("GetRequiredService<global::MyApp.AdminPolicy>", Generated(result), StringComparison.Ordinal);
        // "missing" is still unknown; "admin" and "MinAge" now resolve.
        var unknown = Assert.Single(result.Diagnostics, d => string.Equals(d.Id, "ZAUTH001", StringComparison.Ordinal));
        Assert.Contains("'missing'", unknown.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        // The referenced MinAge policy's type argument matches the argument the source passes.
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZAUTH007", StringComparison.Ordinal));
        Assert.Contains("__p_MinAge.EvaluateAsync(ctx, 18, ct)", Generated(result), StringComparison.Ordinal);
    }

    [Fact]
    public void TrackedSteps_HoldNoSymbolsSyntaxOrCompilations()
    {
        // A model that holds a symbol, a syntax node or a compilation keeps that compilation
        // alive in the cache and never compares equal across edits.
        var compilation = CreateCompilation(
            CSharpSyntaxTree.ParseText(PolicyFile, path: "/src/Policies.cs"),
            CSharpSyntaxTree.ParseText(RequestFile, path: "/src/Requests.cs"));

        var result = CreateDriver().RunGenerators(compilation).GetRunResult();

        foreach (var name in TrackedSteps)
        {
            foreach (var step in result.Results[0].TrackedSteps[name])
            {
                foreach (var (value, _) in step.Outputs)
                {
                    AssertCacheable(value, name, depth: 0);
                }
            }
        }
    }

    private static void AssertCacheable(object? value, string step, int depth)
    {
        if (value is null || depth > 12) return;
        Assert.False(
            value is Compilation or ISymbol or SyntaxNode or SemanticModel or AttributeData or Location,
            $"step {step} holds a {value.GetType().Name}");

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string || value is SyntaxTree || value is TextSpan) return;

        if (value is IEnumerable sequence)
        {
            foreach (var item in sequence) AssertCacheable(item, step, depth + 1);
            return;
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            AssertCacheable(field.GetValue(value), step, depth + 1);
        }
    }

    private static GeneratorDriver CreateDriver() =>
        CSharpGeneratorDriver.Create(
            [new PolicyRegistryGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    private static string Generated(GeneratorDriverRunResult result) =>
        string.Concat(result.GeneratedTrees.Select(t => t.FilePath + "\n" + t.GetText()));

    private static string[] Describe(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics.Select(d => $"{d.Id} {d.Location.GetLineSpan()} {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}").ToArray();

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] trees) =>
        CSharpCompilation.Create(
            "TestAssembly",
            trees,
            StandardReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static List<MetadataReference> StandardReferences()
    {
        _ = typeof(PolicyAttribute).FullName;
        _ = typeof(ZeroAlloc.Results.UnitResult<>).FullName;

        var references = new List<MetadataReference>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location)) continue;
            references.Add(MetadataReference.CreateFromFile(asm.Location));
        }
        return references;
    }
}
