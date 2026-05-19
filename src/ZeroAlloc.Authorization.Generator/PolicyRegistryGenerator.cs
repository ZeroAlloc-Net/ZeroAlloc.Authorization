using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ZeroAlloc.Authorization.Generator.Discovery;
using ZeroAlloc.Authorization.Generator.Emit;

namespace ZeroAlloc.Authorization.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class PolicyRegistryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var compilationProvider = context.CompilationProvider;
        context.RegisterSourceOutput(compilationProvider, GenerateForCompilation);
    }

    private static void GenerateForCompilation(SourceProductionContext spc, Compilation compilation)
    {
        var policies = PolicySymbolWalker.Find(compilation);
        var requires = RequireSymbolWalker.Find(compilation);
        if (policies.Count == 0 && requires.Count == 0) return;

        // Build a name→info dictionary for the emitter.
        // ZAUTH002 (duplicates) will diagnose separately in Task 10 — for now, last-write-wins.
        var byName = new Dictionary<string, PolicyInfo>(System.StringComparer.Ordinal);
        for (int i = 0; i < policies.Count; i++)
        {
            byName[policies[i].Name] = policies[i];
        }

        var authorizers = AuthorizerForEmitter.Emit(requires, byName);
        var registration = DIRegistrationEmitter.Emit(policies, requires);

        spc.AddSource("ZeroAllocAuthorization.Generated.g.cs", SourceText.From(authorizers + registration, System.Text.Encoding.UTF8));
    }
}
