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

    public static readonly DiagnosticDescriptor RequireAnyPolicySingleName = new(
        id: "ZAUTH006",
        title: "[RequireAnyPolicy] with a single policy name",
        messageFormat: "[RequireAnyPolicy(\"{0}\")] on '{1}' lists a single policy — use [RequirePolicy(\"{0}\")] for clarity",
        category: "ZeroAlloc.Authorization",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RequirePolicyArgShapeMismatch = new(
        id: "ZAUTH007",
        title: "[RequirePolicy] argument shape doesn't match policy interface",
        messageFormat: "[RequirePolicy(\"{0}\", ...)] on '{1}': {2}",
        category: "ZeroAlloc.Authorization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PolicyImplementsMultipleVariants = new(
        id: "ZAUTH008",
        title: "[Policy] class implements multiple IAuthorizationPolicy variants",
        messageFormat: "[Policy(\"{0}\")] class '{1}' implements multiple IAuthorizationPolicy variants ({2}); pick one or split into separately-named policies",
        category: "ZeroAlloc.Authorization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
