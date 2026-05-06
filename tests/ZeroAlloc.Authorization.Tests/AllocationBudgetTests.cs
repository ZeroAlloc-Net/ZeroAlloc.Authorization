using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.AotSmoke.Internal;

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
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AllocationGate.AssertBudgetValueTask<int>(
                budgetBytes: 0,
                iterations: 1,
                action: async () => { await Task.Yield(); return 1; },
                label: "yielding"));

        Assert.Contains("sync-completion-required", ex.Message);
    }

    [Fact]
    public void IsAuthorized_AllowPath_ZeroAllocation()
    {
        IAuthorizationPolicy policy = new AdminOnlyPolicy();
        var ctx = NewAdminContext();
        AllocationGate.AssertBudget(0, 1000, () => _ = policy.IsAuthorized(ctx), "IsAuthorized (allow)");
    }

    [Fact]
    public void IsAuthorized_DenyAnonymous_ZeroAllocation()
    {
        IAuthorizationPolicy policy = new AdminOnlyPolicy();
        AllocationGate.AssertBudget(0, 1000,
            () => _ = policy.IsAuthorized(AnonymousSecurityContext.Instance),
            "IsAuthorized (deny anonymous)");
    }

    [Fact]
    public void Evaluate_AllowPath_ZeroAllocation()
    {
        IAuthorizationPolicy policy = new AdminOnlyPolicy();
        var ctx = NewAdminContext();
        AllocationGate.AssertBudget(0, 1000, () => _ = policy.Evaluate(ctx), "Evaluate (allow)");
    }

    [Fact]
    public void IsAuthorizedAsync_AllowPath_ZeroAllocation()
    {
        IAuthorizationPolicy policy = new AdminOnlyPolicy();
        var ctx = NewAdminContext();
        AllocationGate.AssertBudgetValueTask(0, 1000, () => policy.IsAuthorizedAsync(ctx), "IsAuthorizedAsync (allow)");
    }

    [Fact]
    public void EvaluateAsync_AllowPath_ZeroAllocation()
    {
        IAuthorizationPolicy policy = new AdminOnlyPolicy();
        var ctx = NewAdminContext();
        AllocationGate.AssertBudgetValueTask(0, 1000, () => policy.EvaluateAsync(ctx), "EvaluateAsync (allow)");
    }

    private static TestContext NewAdminContext()
        => new("alice", new HashSet<string> { "Admin" }, new Dictionary<string, string>());

    private sealed class AdminOnlyPolicy : IAuthorizationPolicy
    {
        public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
    }

    private sealed record TestContext(string Id,
                                      IReadOnlySet<string> Roles,
                                      IReadOnlyDictionary<string, string> Claims) : ISecurityContext;
}
