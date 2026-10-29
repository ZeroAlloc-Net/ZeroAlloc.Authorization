using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Discovery;

/// <summary>
/// The identity of the compilation the generator runs in, which decides what it can access.
/// </summary>
internal sealed record ScanTarget(string AssemblyName, string PublicKey)
{
    public static ScanTarget From(Compilation compilation)
    {
        var key = compilation.Options.CryptoPublicKey;
        return new ScanTarget(
            compilation.AssemblyName ?? string.Empty,
            key.IsDefaultOrEmpty ? string.Empty : Convert.ToBase64String(key.ToArray()));
    }
}
