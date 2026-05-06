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
}
