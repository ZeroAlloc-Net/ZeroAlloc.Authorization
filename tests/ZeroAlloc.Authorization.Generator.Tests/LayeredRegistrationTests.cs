using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Authorization.Generator.Tests;

/// <summary>
/// In the bundled package the generator runs in every project that references
/// ZeroAlloc.Authorization, so a layered solution runs it once per layer. The layers that declare
/// no policy must neither shadow the registration of a layer that does, nor clash with it.
/// </summary>
public sealed class LayeredRegistrationTests
{
    private const string Policies = """
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Authorization;
        using ZeroAlloc.Results;
        namespace App;

        [Policy("admin")]
        public sealed class AdminPolicy : IAuthorizationPolicy
        {
            public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, CancellationToken ct = default)
                => new(UnitResult<AuthorizationFailure>.Success());
        }

        [RequirePolicy("admin")]
        public sealed record DeleteUser(int Id);
        """;

    private const string Host = """
        using Microsoft.Extensions.DependencyInjection;
        using ZeroAlloc.Authorization.Generated;
        namespace Host;

        public static class Wiring
        {
            public static IServiceCollection Wire(IServiceCollection services) => services.AddZeroAllocAuthorization();
        }
        """;

    private const string Plain = "namespace Plain; public sealed class Nothing { }";

    private sealed record Layer(string Name, Compilation Output, string Generated, IReadOnlyList<Diagnostic> Diagnostics);

    private static Layer Build(string name, string source, params Layer[] references) => Build(name, source, true, references);

    private static Layer Build(string name, string source, bool runGenerator, params Layer[] references)
    {
        var refs = StandardReferences().Concat(references.Select(r => r.Output.ToMetadataReference())).ToList();
        var compilation = CSharpCompilation.Create(
            name,
            new[] { CSharpSyntaxTree.ParseText(source) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var output = (Compilation)compilation;
        var generated = string.Empty;
        if (runGenerator)
        {
            var driver = CSharpGeneratorDriver.Create(new PolicyRegistryGenerator().AsSourceGenerator())
                .RunGeneratorsAndUpdateCompilation(compilation, out output, out _);
            generated = string.Concat(driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
        }
        var problems = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error || d.Id is "CS0436" or "CS0433")
            .ToList();
        return new Layer(name, output, generated, problems);
    }

    // The assembly that declares the method the host's call binds to.
    private static string BoundAssembly(Layer host)
    {
        var tree = host.Output.SyntaxTrees.First();
        var model = host.Output.GetSemanticModel(tree);
        var call = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().First();
        var method = (IMethodSymbol)model.GetSymbolInfo(call).Symbol!;
        return method.ContainingAssembly.Name;
    }

    // True when the host's call ends up registering the admin policy.
    private static bool HostRegistersPolicy(Layer host, params Layer[] layers)
    {
        var assembly = BoundAssembly(host);
        var text = assembly == host.Name ? host.Generated : layers.Single(l => l.Name == assembly).Generated;
        return text.Contains("AddScoped<global::App.AdminPolicy>");
    }

    // The host does not run the generator, as in the split-package setup where only the layer
    // that owns the policies does. It sees the registrations of the layers it references.
    [Fact]
    public void Host_ReferencingPolicyLayer_BindsToItsRegistration()
    {
        var application = Build("Application", Policies);
        var host = Build("Host", Host, false, application);

        Assert.Empty(host.Diagnostics);
        Assert.Equal("Application", BoundAssembly(host));
        Assert.True(HostRegistersPolicy(host, application));
    }

    [Fact]
    public void Host_ReferencingEmptyLowerLayerAndPolicyLayer_BindsToThePolicyLayer()
    {
        var kernel = Build("Kernel", Plain);
        var application = Build("Application", Policies, kernel);
        var host = Build("Host", Host, false, kernel, application);

        Assert.Empty(kernel.Diagnostics);
        Assert.Empty(application.Diagnostics);
        Assert.Empty(host.Diagnostics);
        Assert.Equal("Application", BoundAssembly(host));
        Assert.True(HostRegistersPolicy(host, kernel, application), "the host's call must register the policies of the policy layer");
    }

    [Fact]
    public void LowerLayerWithoutPolicies_KeepsItsRegistrationInternal()
    {
        var kernel = Build("Kernel", Plain);

        Assert.Contains("internal static class GeneratedAuthorizationRegistration", kernel.Generated);
        Assert.DoesNotContain("public static class GeneratedAuthorizationRegistration", kernel.Generated);
        Assert.Contains("TryAddSingleton<global::ZeroAlloc.Authorization.ZeroAllocAuthorizationMarker>", kernel.Generated);
        Assert.Empty(kernel.Diagnostics);
    }

    [Fact]
    public void EmptyProjectWithNoPolicyReferences_CanCallItsOwnRegistration()
    {
        var project = Build("Project", Host);

        Assert.Empty(project.Diagnostics);
        Assert.Equal("Project", BoundAssembly(project));
    }

    [Fact]
    public void EmptyLayerAboveAnotherEmptyLayer_EmitsNothingNew()
    {
        var kernel = Build("Kernel", Plain);
        var middle = Build("Middle", Plain, kernel);

        // Kernel's registration is internal, so Middle cannot see it and emits its own internal one.
        Assert.Contains("internal static class GeneratedAuthorizationRegistration", middle.Generated);
        Assert.Empty(middle.Diagnostics);
    }

    [Fact]
    public void EmptyLayer_WhenSeveralReferencesAlreadyProvideRegistration_EmitsNothing()
    {
        // Registrations that did not come from this generator, in two assemblies: both are public.
        const string Provider = "namespace ZeroAlloc.Authorization.Generated { public static class GeneratedAuthorizationRegistration { } }";
        var first = BuildPlain("ProviderOne", Provider);
        var second = BuildPlain("ProviderTwo", Provider);
        var project = Build("Project", Plain, first, second);

        Assert.Equal(string.Empty, project.Generated);
    }

    private static Layer BuildPlain(string name, string source)
    {
        var compilation = CSharpCompilation.Create(
            name,
            new[] { CSharpSyntaxTree.ParseText(source) },
            StandardReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return new Layer(name, compilation, string.Empty, new List<Diagnostic>());
    }

    private static List<MetadataReference> StandardReferences()
    {
        // The whole trusted platform set, so facades such as the one System.IServiceProvider is
        // forwarded through resolve and the layers compile without errors of their own.
        return ((string)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(System.IO.Path.PathSeparator, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    }
}
