namespace ZeroAlloc.Authorization.AotSmoke.Internal;

internal static class AllocationGate
{
    public static void AssertBudget(int budgetBytes, int iterations, Action action, string label)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));

        // Warmup — JIT-compile, populate type-handle caches, allocate one-time fixtures.
        action();
        action();

        // Flush warmup garbage so it can't leak into the measurement.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) action();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var perCall = allocated / iterations;
        if (perCall > budgetBytes)
        {
            throw new InvalidOperationException(
                $"AllocationGate: {label} allocated {perCall} B/call over {iterations} iterations " +
                $"(total {allocated} B), budget is {budgetBytes} B/call. " +
                "Use BenchmarkDotNet [MemoryDiagnoser] locally to find the culprit.");
        }
    }
}
