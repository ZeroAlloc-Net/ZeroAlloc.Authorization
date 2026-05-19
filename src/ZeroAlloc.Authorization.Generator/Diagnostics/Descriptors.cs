using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Diagnostics;

internal static class Descriptors
{
    private const string Category = "ZeroAlloc.Authorization";

    public static readonly DiagnosticDescriptor UnknownPolicyName = new(
        id: "ZAUTH001",
        title: "Required policy name is not defined",
        messageFormat: "Required policy '{0}' is not defined. Add [Policy(\"{0}\")] to a class implementing IAuthorizationPolicy.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicatePolicyName = new(
        id: "ZAUTH002",
        title: "Duplicate policy name",
        messageFormat: "Duplicate policy name '{0}'. Each [Policy] name must be unique within the compilation.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PolicyDoesNotImplementInterface = new(
        id: "ZAUTH003",
        title: "[Policy] class does not implement IAuthorizationPolicy",
        // RS1032 forbids single-sentence message with trailing period — phrase as 'is X but Y' fragment without period.
        messageFormat: "'{0}' is decorated with [Policy] but does not implement IAuthorizationPolicy",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PolicyNotInstantiable = new(
        id: "ZAUTH004",
        title: "[Policy] class cannot be instantiated by DI",
        // RS1032 forbids single-sentence message with trailing period.
        messageFormat: "'{0}' is decorated with [Policy] but is abstract — DI cannot instantiate it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RequirePolicyInvalidTarget = new(
        id: "ZAUTH005",
        title: "[RequirePolicy] applied to unsupported type kind",
        // RS1032: phrased as 'X. Y.' multi-sentence with trailing period.
        messageFormat: "[RequirePolicy] can only be applied to classes, structs, or records. '{0}' is {1}.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
