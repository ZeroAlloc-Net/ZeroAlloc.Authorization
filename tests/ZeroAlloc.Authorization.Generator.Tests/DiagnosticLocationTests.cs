using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Authorization.Generator.Tests;

/// <summary>
/// Every ZAUTH diagnostic is reported at the class or attribute it is about, as a location bound
/// to the syntax tree, so the IDE can point at it and <c>#pragma warning disable</c> can suppress
/// it. A source marked with [| and |] gives the expected spans; the markers are removed before it
/// runs.
/// </summary>
public sealed class DiagnosticLocationTests
{
    private const string TestFilePath = "/src/App.cs";

    private const string Prelude = """
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Authorization;
        using ZeroAlloc.Results;
        namespace MyApp;

        """;

    private const string Evaluate = """
            public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, CancellationToken ct = default)
                => new(UnitResult<AuthorizationFailure>.Success());
        """;

    private const string AdminPolicy = """
        [Policy("admin")]
        public sealed class AdminPolicy : IAuthorizationPolicy
        {
        """ + "\n" + Evaluate + "\n}\n";

    [Theory]
    // ZAUTH001: the [RequirePolicy] attribute that names the unknown policy.
    [InlineData("ZAUTH001", Prelude + """
        [[|RequirePolicy("nonexistent")|]]
        public sealed record Foo(int Id);
        """)]
    // ZAUTH001: the [RequireAnyPolicy] attribute that names the unknown policy.
    [InlineData("ZAUTH001", Prelude + AdminPolicy + """
        [[|RequireAnyPolicy("admin", "nonexistent")|]]
        public sealed record Foo(int Id);
        """)]
    // ZAUTH003: the [Policy] class identifier.
    [InlineData("ZAUTH003", Prelude + """
        [Policy("admin")]
        public sealed class [|AdminPolicy|] { }
        """)]
    // ZAUTH004: the [Policy] class identifier.
    [InlineData("ZAUTH004", Prelude + """
        [Policy("admin")]
        public abstract class [|AdminPolicy|] : IAuthorizationPolicy
        {
        """ + "\n" + Evaluate + "\n}\n")]
    // ZAUTH005: the type identifier. The attribute's targets already make this CS0592, but the
    // generator still sees the attribute.
    [InlineData("ZAUTH005", Prelude + AdminPolicy + """
        [RequirePolicy("admin")]
        public interface [|IFoo|] { }
        """)]
    // ZAUTH006: the [RequireAnyPolicy] attribute.
    [InlineData("ZAUTH006", Prelude + AdminPolicy + """
        [[|RequireAnyPolicy("admin")|]]
        public sealed record Foo(int Id);
        """)]
    // ZAUTH007: the [RequirePolicy] attribute whose arguments do not fit.
    [InlineData("ZAUTH007", Prelude + """
        [Policy("MinAge")]
        public sealed class MinAgePolicy : IAuthorizationPolicy<int>
        {
            public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, int a, CancellationToken ct = default)
                => new(UnitResult<AuthorizationFailure>.Success());
        }
        [[|RequirePolicy("MinAge")|]]
        public sealed record Foo(int Id);
        """)]
    // ZAUTH008: the [Policy] class identifier.
    [InlineData("ZAUTH008", Prelude + """
        [Policy("Foo")]
        public sealed class [|FooPolicy|] : IAuthorizationPolicy, IAuthorizationPolicy<int>
        {
        """ + "\n" + Evaluate + "\n" + """
            public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, int a, CancellationToken ct = default)
                => new(UnitResult<AuthorizationFailure>.Success());
        }
        """)]
    public void Diagnostic_IsReportedAtItsSourceLocation(string id, string markedSource)
    {
        var (source, spans) = Unmark(markedSource);

        var diagnostics = RunOnFile(source);

        AssertAt(One(diagnostics, id).Location, source, Assert.Single(spans));
    }

    [Fact]
    public void ZAUTH002_IsReportedAtTheLaterPolicy_WithTheEarlierAsAdditionalLocation()
    {
        var (source, spans) = Unmark(Prelude + """
            [[|Policy("admin")|]]
            public sealed class AdminPolicyA : IAuthorizationPolicy
            {
            """ + "\n" + Evaluate + "\n}\n" + """
            [[|Policy("admin")|]]
            public sealed class AdminPolicyB : IAuthorizationPolicy
            {
            """ + "\n" + Evaluate + "\n}\n");

        var diagnostics = RunOnFile(source);

        var diagnostic = One(diagnostics, "ZAUTH002");
        AssertAt(diagnostic.Location, source, spans[1]);
        AssertAt(Assert.Single(diagnostic.AdditionalLocations), source, spans[0]);
    }

    [Fact]
    public void PragmaAroundOnePolicy_SuppressesThatDiagnosticOnly()
    {
        // Both policies report ZAUTH003; the pragma covers Quiet only. The [RequireAnyPolicy]
        // inside the same region reports ZAUTH006, and the pragma does not name it.
        var source = Prelude + """
            #pragma warning disable ZAUTH003
            [Policy("quiet")]
            public sealed class QuietPolicy { }

            [RequireAnyPolicy("loud")]
            public sealed record QuietRequest(int Id);
            #pragma warning restore ZAUTH003

            [Policy("loud")]
            public sealed class LoudPolicy { }
            """;

        var diagnostics = RunOnFile(source);

        var zauth003 = diagnostics.Where(d => string.Equals(d.Id, "ZAUTH003", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, zauth003.Count);
        Assert.True(ForType(zauth003, "QuietPolicy").IsSuppressed);
        Assert.False(ForType(zauth003, "LoudPolicy").IsSuppressed);
        Assert.False(One(diagnostics, "ZAUTH006").IsSuppressed);

        static Diagnostic ForType(List<Diagnostic> list, string typeName) =>
            Assert.Single(list, d => d.GetMessage(CultureInfo.InvariantCulture)
                .Contains("MyApp." + typeName + "'", StringComparison.Ordinal));
    }

    [Fact]
    public void EditAboveAPolicy_MovesItsDiagnostic()
    {
        // The generator reruns on every compilation and reads locations from that compilation's
        // symbols, so a diagnostic follows its code into the new tree.
        const string source = Prelude + """
            [Policy("admin")]
            public sealed class AdminPolicy { }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, path: TestFilePath);
        var compilation = CreateCompilation(tree);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new PolicyRegistryGenerator().AsSourceGenerator());
        driver = driver.RunGenerators(compilation);
        var before = One(driver.GetRunResult().Diagnostics, "ZAUTH003");

        var moved = tree.WithChangedText(SourceText.From(
            source.Replace("namespace MyApp;", "namespace MyApp;\n\n// two\n// more lines", StringComparison.Ordinal)));
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(tree, moved));
        var after = One(driver.GetRunResult().Diagnostics, "ZAUTH003");

        Assert.Same(moved, after.Location.SourceTree);
        Assert.Equal(
            before.Location.GetLineSpan().StartLinePosition.Line + 3,
            after.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void CodeInAReferencedProject_IsReportedWithoutALocation_AndDoesNotFailTheGenerator()
    {
        // In the IDE a project reference is a compilation reference, whose symbols have source
        // locations in the other project's trees. A generator may only report locations in the
        // compilation it runs on; reporting one of those trees fails the generator.
        var library = CSharpCompilation.Create(
            "Library",
            [CSharpSyntaxTree.ParseText(Prelude + """
                [Policy("Foo")]
                public sealed class FooPolicy : IAuthorizationPolicy, IAuthorizationPolicy<int>
                {
                """ + "\n" + Evaluate + "\n" + """
                    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, int a, CancellationToken ct = default)
                        => new(UnitResult<AuthorizationFailure>.Success());
                }
                [RequireAnyPolicy("Foo")]
                public sealed record Cmd(int Id);
                """, path: "/lib/Library.cs")],
            StandardReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilation = CreateCompilation(CSharpSyntaxTree.ParseText(Prelude, path: TestFilePath))
            .AddReferences(library.ToMetadataReference());
        CSharpGeneratorDriver.Create(new PolicyRegistryGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "CS8785", StringComparison.Ordinal));
        Assert.Equal(Location.None, One(diagnostics, "ZAUTH006").Location);
        Assert.Equal(Location.None, One(diagnostics, "ZAUTH008").Location);
    }

    private static ImmutableArray<Diagnostic> RunOnFile(string source)
    {
        var compilation = CreateCompilation(CSharpSyntaxTree.ParseText(source, path: TestFilePath));

        // RunGeneratorsAndUpdateCompilation passes the generator's diagnostics through the
        // compilation's filter, which applies #pragma and sets IsSuppressed.
        CSharpGeneratorDriver.Create(new PolicyRegistryGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        return diagnostics;
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree tree) =>
        CSharpCompilation.Create(
            "TestAssembly",
            [tree],
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

    private static Diagnostic One(ImmutableArray<Diagnostic> diagnostics, string id) =>
        Assert.Single(diagnostics, d => string.Equals(d.Id, id, StringComparison.Ordinal));

    private static void AssertAt(Location location, string source, TextSpan expected)
    {
        // A source location, bound to the tree, is what #pragma and the IDE need.
        Assert.Equal(LocationKind.SourceFile, location.Kind);
        Assert.Equal(TestFilePath, location.SourceTree!.FilePath);
        Assert.Equal(expected, location.SourceSpan);
        Assert.Equal(SourceText.From(source).Lines.GetLinePositionSpan(expected), location.GetLineSpan().Span);
    }

    private static (string source, List<TextSpan> spans) Unmark(string marked)
    {
        var sb = new StringBuilder(marked.Length);
        var spans = new List<TextSpan>();
        var start = -1;
        for (var i = 0; i < marked.Length; i++)
        {
            if (string.CompareOrdinal(marked, i, "[|", 0, 2) == 0)
            {
                start = sb.Length;
                i++;
            }
            else if (string.CompareOrdinal(marked, i, "|]", 0, 2) == 0)
            {
                spans.Add(TextSpan.FromBounds(start, sb.Length));
                i++;
            }
            else
            {
                sb.Append(marked[i]);
            }
        }

        return (sb.ToString(), spans);
    }
}
