using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Discovery;

internal static class TypeWalker
{
    /// <summary>
    /// Visits every type in the namespace, nested types included.
    /// </summary>
    public static void Walk(INamespaceSymbol root, System.Action<INamedTypeSymbol> visit, CancellationToken ct)
    {
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();

            // Nested types pushed via type.GetTypeMembers() are popped here as INamedTypeSymbol.
            // Without visiting them, [Policy] or [RequirePolicy] on a nested type is ignored.
            if (current is INamedTypeSymbol currentType)
            {
                visit(currentType);
            }

            foreach (var member in current.GetMembers())
            {
                if (member is INamespaceSymbol ns)
                {
                    stack.Push(ns);
                }
                else if (member is INamedTypeSymbol type)
                {
                    foreach (var nested in type.GetTypeMembers()) stack.Push(nested);
                    visit(type);
                }
            }
        }
    }
}
