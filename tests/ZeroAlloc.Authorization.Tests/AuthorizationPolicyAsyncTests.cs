using ZeroAlloc.Authorization;

namespace ZeroAlloc.Authorization.Tests;

public class AuthorizationPolicyAsyncTests
{
    private sealed class SyncOnlyPolicy(bool result) : IAuthorizationPolicy
    {
        public bool IsAuthorized(ISecurityContext ctx) => result;
    }

    private sealed class AsyncOverridePolicy : IAuthorizationPolicy
    {
        public bool SyncCalled { get; private set; }
        public bool AsyncCalled { get; private set; }
        public bool IsAuthorized(ISecurityContext ctx) { SyncCalled = true; return false; }
        public ValueTask<bool> IsAuthorizedAsync(ISecurityContext ctx, CancellationToken ct = default)
        {
            AsyncCalled = true;
            return new ValueTask<bool>(true);
        }
    }

    [Fact]
    public async Task AsyncDefault_DelegatesToSync_True()
    {
        IAuthorizationPolicy policy = new SyncOnlyPolicy(true);
        Assert.True(await policy.IsAuthorizedAsync(AnonymousSecurityContext.Instance));
    }

    [Fact]
    public async Task AsyncDefault_DelegatesToSync_False()
    {
        IAuthorizationPolicy policy = new SyncOnlyPolicy(false);
        Assert.False(await policy.IsAuthorizedAsync(AnonymousSecurityContext.Instance));
    }

    [Fact]
    public async Task AsyncOverride_BypassesSync()
    {
        var policy = new AsyncOverridePolicy();
        var result = await ((IAuthorizationPolicy)policy).IsAuthorizedAsync(AnonymousSecurityContext.Instance);
        Assert.True(result);
        Assert.True(policy.AsyncCalled);
        Assert.False(policy.SyncCalled);
    }

    [Fact]
    public async Task AsyncCancellation_ThrowsOperationCanceled()
    {
        var policy = new SlowAsyncPolicy();
        using var cts = new CancellationTokenSource();
        var task = ((IAuthorizationPolicy)policy).IsAuthorizedAsync(AnonymousSecurityContext.Instance, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task.ConfigureAwait(false));
    }

    private sealed class SlowAsyncPolicy : IAuthorizationPolicy
    {
        public bool IsAuthorized(ISecurityContext ctx) => true;
        public async ValueTask<bool> IsAuthorizedAsync(ISecurityContext ctx, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return true;
        }
    }
}
