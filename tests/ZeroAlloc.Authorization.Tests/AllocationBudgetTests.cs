using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.TestHelpers;
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization.Tests;

public class AllocationBudgetTests
{
    // The self-tests' allocations escape to this field. The gate measures optimised code, and
    // since .NET 10 the JIT puts an object that never leaves the method on the stack, so a
    // discarded `_ = new object()` allocates nothing there.
    private static object? s_sink;

    [Fact]
    public void Gate_DetectsAllocation_WhenActionAllocates()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AllocationGate.AssertBudget(
                budgetBytes: 0,
                iterations: 1000,
                action: () => s_sink = new object(),
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
        // The helper makes 2,000 warmup calls, then measures windows until the result is
        // stable and keeps the least window. An action that allocates only on its first call
        // (e.g. lazy field init) should be absorbed by warmup and pass a strict 0 B budget. If
        // a future refactor skips the warmup, this test fires.
        var firstCall = true;
        AllocationGate.AssertBudget(0, 1000, () =>
        {
            if (firstCall) { firstCall = false; s_sink = new object(); }
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
    public void Parameterized_EvaluateAsync_ZeroAllocation()
    {
        // v2.1: [RequirePolicy("MinAge", 18)] dispatch. The generator emits the int arg as a
        // literal and calls IAuthorizationPolicy<int>.EvaluateAsync with it typed: no boxing,
        // no object[], and the synchronously completed ValueTask<UnitResult<...>> is a struct.
        // A regression that boxes the typed arg adds ~24 B per arg and fails this gate.
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var authorizer = scope.ServiceProvider
            .GetRequiredService<AuthorizerFor<MinAgeProtectedRequest>>();
        // Age >= 18 -> Success path: avoids the policy's in-failure string interpolation
        // so the gate isolates the dispatch cost.
        var ctx = new AgedTestContext("alice", age: 21);

        AllocationGate.AssertBudgetValueTask(0, 1000,
            () => authorizer.EvaluateAsync(ctx),
            "[Parameterized] AuthorizerFor<MinAgeProtectedRequest>.EvaluateAsync (literal 18 dispatch)");
    }

    [Fact]
    public void AnyOf_EvaluateAsync_ZeroAllocation()
    {
        // [RequireAnyPolicy("AllowOr", "ProbeSecond")]: the first candidate succeeds, so the
        // OR group short-circuits. The dispatch must not allocate on that path.
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var authorizer = scope.ServiceProvider
            .GetRequiredService<AuthorizerFor<OrCompositionFirstSucceedsRequest>>();

        AllocationGate.AssertBudgetValueTask(0, 1000,
            () => authorizer.EvaluateAsync(AnonymousSecurityContext.Instance),
            "[RequireAnyPolicy] AuthorizerFor<OrCompositionFirstSucceedsRequest>.EvaluateAsync (first succeeds)");
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
