using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization.Tests;

/// <summary>
/// Runtime semantics for [RequireAnyPolicy]:
///   * First listed policy succeeding short-circuits the OR group.
///   * All-fail produces a combined AuthorizationFailure with
///     Code = "any.all_failed" and Reason in [Name: ...] OR [Name: ...] format.
/// Exercises the v2.1 generator emit end-to-end through the DI container.
/// </summary>
public sealed class OrCompositionTests
{
    private static readonly TestContext Ctx = new("alice",
        new HashSet<string>(),
        new Dictionary<string, string>());

    [Fact]
    public async Task FirstPolicySucceeds_ShortCircuits_ReturnsSuccess()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var authorizer = scope.ServiceProvider
            .GetRequiredService<AuthorizerFor<OrCompositionFirstSucceedsRequest>>();

        var result = await authorizer.EvaluateAsync(Ctx);

        Assert.True(result.IsSuccess);
        // Short-circuit guarantee: the second policy MUST NOT have been invoked.
        Assert.Equal(0, ProbeSecondPolicy.InvocationCount);
    }

    [Fact]
    public async Task AllPoliciesFail_ReturnsCombinedFailure_WithAllFailedCode()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var authorizer = scope.ServiceProvider
            .GetRequiredService<AuthorizerFor<OrCompositionAllFailRequest>>();

        var result = await authorizer.EvaluateAsync(Ctx);

        Assert.False(result.IsSuccess);
        Assert.Equal("any.all_failed", result.Error.Code);
        var reason = result.Error.Reason;
        Assert.NotNull(reason);
        // Declaration-order: DenyA then DenyB. Reason format is
        //   "[DenyA: reasonA] OR [DenyB: reasonB]"
        Assert.Contains("[DenyA: deny-a-reason]", reason!, StringComparison.Ordinal);
        Assert.Contains("[DenyB: deny-b-reason]", reason!, StringComparison.Ordinal);
        Assert.Contains(" OR ", reason!, StringComparison.Ordinal);
        Assert.True(
            reason!.IndexOf("[DenyA", StringComparison.Ordinal)
            < reason!.IndexOf("[DenyB", StringComparison.Ordinal),
            "Per-policy failures must appear in declaration order.");
    }

    private sealed record TestContext(string Id,
                                      IReadOnlySet<string> Roles,
                                      IReadOnlyDictionary<string, string> Claims) : ISecurityContext;
}

[Policy("AllowOr")]
internal sealed class AllowOrPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(UnitResult<AuthorizationFailure>.Success());
}

/// <summary>A policy that records how many times it was invoked. Tests assert this
/// stays at zero when an earlier policy in the OR group succeeds.</summary>
[Policy("ProbeSecond")]
internal sealed class ProbeSecondPolicy : IAuthorizationPolicy
{
    private static int s_count;
    public static int InvocationCount => s_count;

    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
    {
        System.Threading.Interlocked.Increment(ref s_count);
        return new ValueTask<UnitResult<AuthorizationFailure>>(
            UnitResult<AuthorizationFailure>.Success());
    }
}

[Policy("DenyA")]
internal sealed class DenyAPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(new AuthorizationFailure("policy.deny.a", "deny-a-reason"));
}

[Policy("DenyB")]
internal sealed class DenyBPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(new AuthorizationFailure("policy.deny.b", "deny-b-reason"));
}

[RequireAnyPolicy("AllowOr", "ProbeSecond")]
internal sealed record OrCompositionFirstSucceedsRequest;

[RequireAnyPolicy("DenyA", "DenyB")]
internal sealed record OrCompositionAllFailRequest;
