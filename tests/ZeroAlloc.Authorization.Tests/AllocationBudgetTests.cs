using ZeroAlloc.Authorization;
using ZeroAlloc.TestHelpers;
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization.Tests;

public class AllocationBudgetTests
{
    [Fact]
    public void Gate_DetectsAllocation_WhenActionAllocates()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AllocationGate.AssertBudget(
                budgetBytes: 0,
                iterations: 1000,
                action: () => _ = new object(),
                label: "test-allocator"));

        Assert.Contains("test-allocator", ex.Message);
        Assert.Contains("budget is 0", ex.Message);
    }

    [Fact]
    public void Gate_RejectsValueTask_NotCompletedSynchronously()
    {
        // A TaskCompletionSource whose Task is never completed — guarantees the
        // ValueTask returns IsCompletedSuccessfully = false on every invocation,
        // regardless of platform/threadpool race conditions. (An async lambda
        // with `await Task.Yield()` raced under Linux CI: the continuation could
        // run before Drain checked IsCompletedSuccessfully.)
        var pending = new TaskCompletionSource<int>();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AllocationGate.AssertBudgetValueTask<int>(
                budgetBytes: 0,
                iterations: 1,
                action: () => new ValueTask<int>(pending.Task),
                label: "pending"));

        Assert.Contains("sync-completion-required", ex.Message);
    }

    [Fact]
    public void Gate_TolerantOfWarmupOnlyAllocations()
    {
        // The helper performs 2 warmup calls before measuring. An action that allocates
        // only on its first call (e.g. lazy field init) should be absorbed by warmup
        // and pass a strict 0 B budget. If a future refactor skips the warmup, this
        // test fires.
        var firstCall = true;
        AllocationGate.AssertBudget(0, 1000, () =>
        {
            if (firstCall) { firstCall = false; _ = new object(); }
        }, "warmup-only-allocator");
    }

    [Fact]
    public void EvaluateAsync_AllowPath_ZeroAllocation()
    {
        IAuthorizationPolicy policy = new AdminOnlyPolicy();
        var ctx = NewAdminContext();
        AllocationGate.AssertBudgetValueTask(0, 1000, () => policy.EvaluateAsync(ctx), "EvaluateAsync (allow)");
    }

    [Fact]
    public void EvaluateAsync_DenyAnonymous_ZeroAllocation()
    {
        IAuthorizationPolicy policy = new AdminOnlyPolicy();
        AllocationGate.AssertBudgetValueTask(0, 1000,
            () => policy.EvaluateAsync(AnonymousSecurityContext.Instance),
            "EvaluateAsync (deny anonymous)");
    }

    private static TestContext NewAdminContext()
        => new("alice", new HashSet<string> { "Admin" }, new Dictionary<string, string>());

    private sealed class AdminOnlyPolicy : IAuthorizationPolicy
    {
        public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
            ISecurityContext ctx, CancellationToken ct = default)
            => new(ctx.Roles.Contains("Admin")
                ? UnitResult<AuthorizationFailure>.Success()
                : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode));
    }

    private sealed record TestContext(string Id,
                                      IReadOnlySet<string> Roles,
                                      IReadOnlyDictionary<string, string> Claims) : ISecurityContext;
}
