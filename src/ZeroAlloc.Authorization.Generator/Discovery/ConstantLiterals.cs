using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Authorization.Generator.Discovery;

/// <summary>
/// Writes an attribute argument as the C# expression the generated dispatcher passes on.
/// </summary>
/// <remarks>
/// The expression must compile, whatever the value, and mean the same in every culture the
/// generator runs in.
/// </remarks>
internal static class ConstantLiterals
{
    public static string Format(TypedConstant c)
    {
        switch (c.Kind)
        {
            case TypedConstantKind.Primitive:
                return FormatPrimitive(c.Value);

            case TypedConstantKind.Enum:
                // A negative value needs parentheses: (global::E)-1 parses as a subtraction.
                var value = string.Format(CultureInfo.InvariantCulture, "{0}", c.Value);
                var cast = "(" + c.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")";
                return value.StartsWith("-", System.StringComparison.Ordinal) ? cast + "(" + value + ")" : cast + value;

            case TypedConstantKind.Type:
                return c.Value is ITypeSymbol type ? "typeof(" + TypeOfOperand(type) + ")" : "null";

            case TypedConstantKind.Array:
                return c.IsNull ? "null" : FormatArray(c);

            default:
                return "default";
        }
    }

    private static string FormatPrimitive(object? value) => value switch
    {
        null => "null",
        string s => SymbolDisplay.FormatLiteral(s, quote: true),
        char ch => SymbolDisplay.FormatLiteral(ch, quote: true),
        bool b => b ? "true" : "false",
        float f when float.IsNaN(f) => "global::System.Single.NaN",
        float f when float.IsPositiveInfinity(f) => "global::System.Single.PositiveInfinity",
        float f when float.IsNegativeInfinity(f) => "global::System.Single.NegativeInfinity",
        // Without the suffix the literal is a double, which does not convert to float.
        float f => f.ToString("R", CultureInfo.InvariantCulture) + "F",
        double d when double.IsNaN(d) => "global::System.Double.NaN",
        double d when double.IsPositiveInfinity(d) => "global::System.Double.PositiveInfinity",
        double d when double.IsNegativeInfinity(d) => "global::System.Double.NegativeInfinity",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        _ => string.Format(CultureInfo.InvariantCulture, "{0}", value),
    };

    private static string TypeOfOperand(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true } named && named.IsDefinition
            // typeof(List<>) arrives as the definition, which displays as List<T>.
            ? named.ConstructUnboundGenericType().ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string FormatArray(TypedConstant c)
    {
        var sb = new StringBuilder("new ");
        sb.Append(c.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Append(" {");
        for (var i = 0; i < c.Values.Length; i++)
        {
            sb.Append(i == 0 ? " " : ", ").Append(Format(c.Values[i]));
        }
        return sb.Append(" }").ToString();
    }
}
