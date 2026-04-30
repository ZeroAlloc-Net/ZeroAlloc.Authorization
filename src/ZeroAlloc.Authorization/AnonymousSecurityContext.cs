using System.Collections.Frozen;

namespace ZeroAlloc.Authorization;

/// <summary>Singleton anonymous caller — no roles, no claims. Default when no
/// caller-provider is configured.</summary>
public sealed class AnonymousSecurityContext : ISecurityContext
{
    /// <summary>The literal value of <see cref="ISecurityContext.Id"/> on the anonymous singleton.
    /// Hosts MAY compare against this constant, but prefer reference-equality with
    /// <see cref="Instance"/>.</summary>
    public const string AnonymousId = "anonymous";

    /// <summary>Shared singleton instance used whenever no caller identity is configured.</summary>
    public static readonly AnonymousSecurityContext Instance = new();

    private AnonymousSecurityContext() { }

    /// <inheritdoc />
    public string Id => AnonymousId;

    /// <inheritdoc />
    public IReadOnlySet<string> Roles { get; } = FrozenSet<string>.Empty;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Claims { get; } = FrozenDictionary<string, string>.Empty;
}
