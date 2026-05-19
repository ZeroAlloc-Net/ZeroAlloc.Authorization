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
}
