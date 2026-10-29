using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Authorization.Generator.Tests;

/// <summary>
/// The <c>ZeroAllocAuthorizationRegistration</c> MSBuild property chooses the accessibility of the
/// generated registration class. A library that declares policies sets it to <c>internal</c>, so
/// its registration does not clash with the one of an app that references it.
/// </summary>
public sealed class RegistrationAccessibilityTests
{
    private const string PublicClass = "public static class GeneratedAuthorizationRegistration";
    private const string InternalClass = "internal static class GeneratedAuthorizationRegistration";

    private const string Policies = """
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

        [RequirePolicy("admin")]
        public sealed record DeleteUser(int Id);
        """;

    private const string Empty = "namespace MyApp; public sealed class Nothing { }";

    [Theory]
    [InlineData("internal")]
    [InlineData("Internal")]
    [InlineData("INTERNAL")]
    [InlineData(" internal ")]
    public void Internal_WithPolicies_EmitsInternalRegistration(string value)
    {
        var (generated, diagnostics, compiled) = Run(Policies, value);

        Assert.Contains(InternalClass, generated);
        Assert.DoesNotContain(PublicClass, generated);
        Assert.Contains("AddScoped<global::MyApp.AdminPolicy>", generated);
        Assert.Empty(diagnostics);
        Assert.Empty(compiled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("public")]
    [InlineData("Public")]
    public void PublicOrUnset_WithPolicies_EmitsTheDefaultPublicRegistration(string? value)
    {
        var (unset, _, _) = Run(Policies, registration: null);
        var (generated, diagnostics, compiled) = Run(Policies, value);

        Assert.Contains(PublicClass, generated);
        Assert.Equal(unset, generated);
        Assert.Empty(diagnostics);
        Assert.Empty(compiled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("public")]
    [InlineData("internal")]
    public void EmptyCompilation_StaysInternal_WhateverTheProperty(string? value)
    {
        var (generated, diagnostics, compiled) = Run(Empty, value);

        Assert.Contains(InternalClass, generated);
        Assert.Contains("TryAddSingleton<global::ZeroAlloc.Authorization.ZeroAllocAuthorizationMarker>", generated);
        Assert.Empty(diagnostics);
        Assert.Empty(compiled);
    }

    [Theory]
    [InlineData("protected")]
    [InlineData("private")]
    [InlineData("yes")]
    public void InvalidValue_ReportsZAUTH009_AndFallsBackToTheDefault(string value)
    {
        var (generated, diagnostics, compiled) = Run(Policies, value);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ZAUTH009", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains($"'{value}'", message);
        Assert.Contains("'public'", message);
        Assert.Contains("'internal'", message);
        Assert.Contains(PublicClass, generated);
        Assert.Empty(compiled);
    }

    [Fact]
    public void InvalidValue_OnAnEmptyCompilation_ReportsZAUTH009()
    {
        var (generated, diagnostics, _) = Run(Empty, "protected");

        Assert.Equal("ZAUTH009", Assert.Single(diagnostics).Id);
        Assert.Contains(InternalClass, generated);
    }

    // The generated source, the generator's diagnostics, and the errors of the compilation with it.
    private static (string Generated, IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<Diagnostic> Compiled) Run(string source, string? registration)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            StandardReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
                new[] { new PolicyRegistryGenerator().AsSourceGenerator() },
                optionsProvider: BuildProperties.Registration(registration))
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var generated = string.Concat(driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        return (generated, diagnostics, errors);
    }

    private static List<MetadataReference> StandardReferences()
    {
        // The whole trusted platform set, so facades such as the one System.IServiceProvider is
        // forwarded through resolve and the generated code compiles.
        return ((string)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(System.IO.Path.PathSeparator, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    }
}
