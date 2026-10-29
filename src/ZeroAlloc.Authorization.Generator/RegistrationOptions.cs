using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Authorization.Generator;

/// <summary>
/// The project's registration settings, as a value the pipeline can cache.
/// </summary>
/// <param name="Accessibility">The accessibility to emit.</param>
/// <param name="InvalidValue">The property's value when it is not one of the allowed ones, reported as ZAUTH009; otherwise null.</param>
internal readonly record struct RegistrationOptions(RegistrationAccessibility Accessibility, string? InvalidValue)
{
    public const string PropertyName = "ZeroAllocAuthorizationRegistration";
    private const string BuildPropertyKey = "build_property." + PropertyName;

    public static RegistrationOptions From(AnalyzerConfigOptionsProvider options)
    {
        if (!options.GlobalOptions.TryGetValue(BuildPropertyKey, out var raw)) return default;
        return Parse(raw);
    }

    public static RegistrationOptions Parse(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value)) return default;
        if (string.Equals(value, "public", System.StringComparison.OrdinalIgnoreCase)) return new RegistrationOptions(RegistrationAccessibility.Public, null);
        if (string.Equals(value, "internal", System.StringComparison.OrdinalIgnoreCase)) return new RegistrationOptions(RegistrationAccessibility.Internal, null);
        return new RegistrationOptions(RegistrationAccessibility.Default, value);
    }

    /// <summary>
    /// The accessibility of the registration class. A compilation with no policies or requests
    /// always gets an internal one: it exists only so a composition root can call it before the
    /// first policy, and must not clash with the registration of a layer that has policies.
    /// </summary>
    public string AccessibilityFor(bool isEmpty) =>
        isEmpty || Accessibility == RegistrationAccessibility.Internal ? "internal" : "public";
}
