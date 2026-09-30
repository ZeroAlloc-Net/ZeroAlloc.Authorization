using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Authorization.Generator.Tests;

/// <summary>
/// The generated dispatcher compiles for every argument a [RequirePolicy] can pass, and for
/// requests declared across partial declarations.
/// </summary>
public sealed class GeneratedCodeCompilesTests
{
    private const string Prelude = """
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Authorization;
        using ZeroAlloc.Results;
        namespace MyApp;

        public enum Level { Low = -1, High = 1 }

        """;

    private static string Policy(string name, string typeArg) => $$"""
        [Policy("{{name}}")]
        public sealed class {{name}}Policy : IAuthorizationPolicy<{{typeArg}}>
        {
            public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, {{typeArg}} a, CancellationToken ct = default)
                => new(UnitResult<AuthorizationFailure>.Success());
        }

        """;

    private const string AdminPolicy = """
        [Policy("Admin")]
        public sealed class AdminPolicy : IAuthorizationPolicy
        {
            public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, CancellationToken ct = default)
                => new(UnitResult<AuthorizationFailure>.Success());
        }

        """;

    private const string OtherPolicy = """
        [Policy("Other")]
        public sealed class OtherPolicy : IAuthorizationPolicy
        {
            public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, CancellationToken ct = default)
                => new(UnitResult<AuthorizationFailure>.Success());
        }

        """;

    [Theory]
    [InlineData("float", "1.5f", "1.5F")]
    [InlineData("double", "0.1", "0.1")]
    [InlineData("Level", "Level.Low", "(global::MyApp.Level)(-1)")]
    [InlineData("Level", "Level.High", "(global::MyApp.Level)1")]
    [InlineData("string", "\"a\\\"b\\\\c\\nd\"", "\"a\\\"b\\\\c\\nd\"")]
    [InlineData("char", "'\\''", "'\\''")]
    [InlineData("long", "-5L", "-5")]
    [InlineData("System.Type", "typeof(int)", "typeof(int)")]
    [InlineData("System.Type", "typeof(System.Collections.Generic.List<>)", "typeof(global::System.Collections.Generic.List<>)")]
    [InlineData("int[]", "new[] { 1, 2 }", "new int[] { 1, 2 }")]
    public void ArgumentIsPassedAsACompilingLiteral(string typeArg, string argument, string expected)
    {
        var source = Prelude + Policy("P", typeArg) + $$"""
            [RequirePolicy("P", {{argument}})]
            public sealed record Cmd(int Id);
            """;

        var generated = RunAndCompile(source);

        Assert.Contains($"EvaluateAsync(ctx, {expected}, ct)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void NullArgumentArray_IsAParameterlessCall()
    {
        var source = Prelude + AdminPolicy + """
            [RequirePolicy("Admin", null)]
            public sealed record Cmd(int Id);
            """;

        var generated = RunAndCompile(source);

        Assert.Contains("__p_Admin.EvaluateAsync(ctx, ct)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void NullNameArray_IsIgnored()
    {
        var source = Prelude + AdminPolicy + """
            [RequireAnyPolicy(null)]
            [RequirePolicy("Admin")]
            public sealed record Cmd(int Id);
            """;

        var generated = RunAndCompile(source);

        Assert.Contains("__p_Admin.EvaluateAsync(ctx, ct)", generated, StringComparison.Ordinal);
    }

    [Theory]
    // The same policy required twice.
    [InlineData("""[RequirePolicy("Admin")] [RequirePolicy("Admin")]""", "__p_Admin_2")]
    // The same name twice in one OR group is evaluated once.
    [InlineData("""[RequireAnyPolicy("Admin", "Admin", "Other")]""", null)]
    // The same name in an AND group and an OR group.
    [InlineData("""[RequirePolicy("Admin")] [RequireAnyPolicy("Admin", "Other")] [RequireAnyPolicy("Admin", "Other")]""", "__p_Admin_MyApp_Cmd_g2")]
    public void RepeatedPolicyName_GetsLocalsOfItsOwn(string attributes, string? expectedLocal)
    {
        var generated = RunAndCompile(Prelude + AdminPolicy + OtherPolicy + attributes + """

            public sealed record Cmd(int Id);
            """);

        if (expectedLocal is not null) Assert.Contains(expectedLocal + " ", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void PolicyNamesThatSanitizeAlike_GetLocalsOfTheirOwn()
    {
        var generated = RunAndCompile(Prelude + """
            [Policy("a-b")]
            public sealed class Dash : IAuthorizationPolicy
            {
                public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, CancellationToken ct = default)
                    => new(UnitResult<AuthorizationFailure>.Success());
            }

            [Policy("a_b")]
            public sealed class Underscore : IAuthorizationPolicy
            {
                public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, CancellationToken ct = default)
                    => new(UnitResult<AuthorizationFailure>.Success());
            }

            [RequirePolicy("a-b")]
            [RequirePolicy("a_b")]
            [RequireAnyPolicy("a-b", "a_b")]
            public sealed record Cmd(int Id);
            """);

        Assert.Contains("__p_a_b_2 ", generated, StringComparison.Ordinal);
        Assert.Contains("__p_a_b_MyApp_Cmd_g2_2 ", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void PartialRequest_GetsOneAuthorizer_WithTheGroupsOfEveryDeclaration()
    {
        var generated = RunAndCompile(
            Prelude + AdminPolicy + OtherPolicy + """
                [RequirePolicy("Admin")]
                public sealed partial record Cmd(int Id);
                """,
            """
            using ZeroAlloc.Authorization;
            namespace MyApp;

            [RequireAnyPolicy("Admin", "Other")]
            public sealed partial record Cmd;
            """);

        var authorizers = generated.Split("class GeneratedAuthorizerFor_MyApp_Cmd").Length - 1;
        Assert.Equal(1, authorizers);
        Assert.Contains("__p_Admin.EvaluateAsync(ctx, ct)", generated, StringComparison.Ordinal);
        Assert.Contains("// OR group MyApp_Cmd_g1", generated, StringComparison.Ordinal);
    }

    private static string RunAndCompile(params string[] sources)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, path: $"/src/File{i}.cs")),
            StandardReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(new PolicyRegistryGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var generated = string.Concat(output.SyntaxTrees.Skip(sources.Length).Select(t => t.ToString()));
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.ToString())) +"\n" + generated);
        return generated;
    }

    // Every assembly the test host can load, the DI abstractions the generated code calls
    // included, so the output compiles as it would in an app.
    private static IEnumerable<MetadataReference> StandardReferences() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path));
}
