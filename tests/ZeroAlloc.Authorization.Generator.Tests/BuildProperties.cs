using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Authorization.Generator.Tests;

/// <summary>
/// The MSBuild properties a generator sees, as the compiler hands them over: global options named
/// <c>build_property.&lt;Name&gt;</c>, for the properties the package marks compiler-visible.
/// </summary>
internal sealed class BuildProperties : AnalyzerConfigOptionsProvider
{
    private readonly Options _global;

    private BuildProperties(Dictionary<string, string> values) => _global = new Options(values);

    public override AnalyzerConfigOptions GlobalOptions => _global;

    /// <summary>The options for a project that sets <c>ZeroAllocAuthorizationRegistration</c>, or none when null.</summary>
    public static BuildProperties Registration(string? value)
    {
        var values = new Dictionary<string, string>(System.StringComparer.Ordinal);
        if (value is not null) values["build_property.ZeroAllocAuthorizationRegistration"] = value;
        return new BuildProperties(values);
    }

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options.Empty;

    private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public static readonly Options Empty = new(new Dictionary<string, string>(System.StringComparer.Ordinal));

        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
    }
}
