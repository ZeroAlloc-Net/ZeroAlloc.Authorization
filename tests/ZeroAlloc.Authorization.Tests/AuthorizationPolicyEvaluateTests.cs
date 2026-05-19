using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization.Tests;

public class AuthorizationPolicyEvaluateTests
{
    private static readonly TestContext Ctx = new("alice",
        new HashSet<string> { "Admin" },
        new Dictionary<string, string>());

    [Fact]
    public async Task EvaluateAsync_AllowingPolicy_Succeeds()
    {
        IAuthorizationPolicy policy = new AllowingPolicy();
        var result = await policy.EvaluateAsync(Ctx);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EvaluateAsync_DenyingPolicy_FailsWithDefaultDenyCode()
    {
        IAuthorizationPolicy policy = new DenyingPolicy();
        var result = await policy.EvaluateAsync(Ctx);
        Assert.False(result.IsSuccess);
        Assert.Equal(AuthorizationFailure.DefaultDenyCode, result.Error.Code);
        Assert.Null(result.Error.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_RichDenyPolicy_EmitsCustomCodeAndReason()
    {
        IAuthorizationPolicy policy = new RichDenyPolicy();
        var result = await policy.EvaluateAsync(Ctx);
        Assert.False(result.IsSuccess);
        Assert.Equal("policy.deny.role", result.Error.Code);
        Assert.Equal("user is not Admin", result.Error.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_AsyncPolicy_AwaitsAndSucceeds()
    {
        IAuthorizationPolicy policy = new AsyncAllowingPolicy();
        var result = await policy.EvaluateAsync(Ctx);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EvaluateAsync_PreCancelledToken_ThrowsOperationCanceled()
    {
        IAuthorizationPolicy policy = new SlowPolicy();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await policy.EvaluateAsync(Ctx, cts.Token).ConfigureAwait(false));
    }

    private sealed class AllowingPolicy : IAuthorizationPolicy
    {
        public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
            ISecurityContext ctx, CancellationToken ct = default)
            => new(UnitResult<AuthorizationFailure>.Success());
    }

    private sealed class DenyingPolicy : IAuthorizationPolicy
    {
        public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
            ISecurityContext ctx, CancellationToken ct = default)
            => new(new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode));
    }

    private sealed class RichDenyPolicy : IAuthorizationPolicy
    {
        public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
            ISecurityContext ctx, CancellationToken ct = default)
            => new(new AuthorizationFailure("policy.deny.role", "user is not Admin"));
    }

    private sealed class AsyncAllowingPolicy : IAuthorizationPolicy
    {
        public async ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
            ISecurityContext ctx, CancellationToken ct = default)
        {
            await Task.Yield();
            return UnitResult<AuthorizationFailure>.Success();
        }
    }

    private sealed class SlowPolicy : IAuthorizationPolicy
    {
        public async ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
            ISecurityContext ctx, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return UnitResult<AuthorizationFailure>.Success();
        }
    }

    private sealed record TestContext(string Id,
                                      IReadOnlySet<string> Roles,
                                      IReadOnlyDictionary<string, string> Claims) : ISecurityContext;
}
