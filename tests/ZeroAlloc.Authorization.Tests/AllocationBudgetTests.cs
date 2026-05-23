using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
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

    [Fact]
    public void Parameterized_EvaluateAsync_Allocates_Within_Budget()
    {
        // v2.1: [RequirePolicy("MinAge", 18)] dispatch — the int arg is emitted as a
        // literal in the generator output (no boxing, no object[]). Measured ~24 B/call
        // on net10.0 Release: that's the ValueTask<UnitResult<...>> wrap returned by
        // AuthorizerFor<T>.EvaluateAsync; the typed int `18` itself contributes 0 bytes.
        // The 256 B/call budget leaves headroom for jitter while still catching a
        // regression that boxes the typed arg (would add ~24 B per arg).
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var authorizer = scope.ServiceProvider
            .GetRequiredService<AuthorizerFor<MinAgeProtectedRequest>>();
        // Age >= 18 -> Success path: avoids the policy's in-failure string interpolation
        // so the gate isolates the dispatch cost (DI handoff + ValueTask wrap).
        var ctx = new AgedTestContext("alice", age: 21);

        AllocationGate.AssertBudgetValueTask(256, 100,
            () => authorizer.EvaluateAsync(ctx),
            "[Parameterized] AuthorizerFor<MinAgeProtectedRequest>.EvaluateAsync (literal 18 dispatch)");
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
