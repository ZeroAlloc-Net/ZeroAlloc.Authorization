using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.TestHelpers;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Results;

var ctx = new TestContext("alice",
    new HashSet<string> { "Admin" },
    new Dictionary<string, string>());
var policy = new AdminOnlyPolicy();

if (!(await ((IAuthorizationPolicy)policy).EvaluateAsync(ctx)).IsSuccess)
    throw new("EvaluateAsync regressed");

if ((await ((IAuthorizationPolicy)policy).EvaluateAsync(AnonymousSecurityContext.Instance)).IsSuccess)
    throw new("Anonymous should be denied");

Console.WriteLine("AOT behavior OK");

// Allocation budget gate — run AFTER behavior assertions so behavior failures are reported first.
// Asserts the README's "Zero allocation on the hot-path EvaluateAsync method" claim under the
// AOT runtime (catches trim/escape-analysis regressions the JIT-side test misses).
AllocationGate.AssertBudgetValueTask(0, 1000,
    () => ((IAuthorizationPolicy)policy).EvaluateAsync(ctx),
    "EvaluateAsync (allow)");
AllocationGate.AssertBudgetValueTask(0, 1000,
    () => ((IAuthorizationPolicy)policy).EvaluateAsync(AnonymousSecurityContext.Instance),
    "EvaluateAsync (deny anonymous)");

Console.WriteLine("AOT allocation gate OK");

// ----------------------------------------------------------------------------
// [RequirePolicy] / AuthorizerFor<T> scenario — exercises the generator-emitted
// DI registration and per-request authorizer subclass under AOT. Validates the
// design's "≤ 0 bytes per happy-path EvaluateAsync" claim end-to-end.
// ----------------------------------------------------------------------------
var services = new ServiceCollection();
services.AddZeroAllocAuthorization();
using var sp = services.BuildServiceProvider();

using (var scope = sp.CreateScope())
{
    var anonCtx = AnonymousSecurityContext.Instance;
    var authorizer = scope.ServiceProvider.GetRequiredService<AuthorizerFor<AotSmokeRequest>>();

    // Behavior assertion — happy path must succeed.
    var warm = authorizer.EvaluateAsync(anonCtx).AsTask().GetAwaiter().GetResult();
    if (!warm.IsSuccess) throw new("AuthorizerFor<AotSmokeRequest> happy path regressed");

    // Allocation gate — the AOT-published binary must round-trip an authorizer
    // dispatch through DI with zero managed allocation per call. The scope and
    // authorizer are resolved ONCE outside the measured loop; we only measure
    // the EvaluateAsync hot path itself.
    AllocationGate.AssertBudgetValueTask(0, 1000,
        () => authorizer.EvaluateAsync(anonCtx),
        "AuthorizerFor<AotSmokeRequest>.EvaluateAsync (allow)");

    Console.WriteLine("AOT [RequirePolicy] allocation gate OK");

    // ------------------------------------------------------------------------
    // v2.1 - [RequireAnyPolicy] short-circuit on first success.
    // Premium succeeds, Trusted denies — the OR group must surface success
    // because at least one candidate passed.
    // ------------------------------------------------------------------------
    var orAuth = scope.ServiceProvider
        .GetRequiredService<AuthorizerFor<OrRequest>>();
    var orResult = await orAuth.EvaluateAsync(AnonymousSecurityContext.Instance);
    if (!orResult.IsSuccess)
        throw new InvalidOperationException("[RequireAnyPolicy] AOT smoke: expected success");

    Console.WriteLine("AOT [RequireAnyPolicy] OK");

    // ------------------------------------------------------------------------
    // v2.1 - parameterized [RequirePolicy("MinAge", 18)] dispatch through the
    // generator-emitted IAuthorizationPolicy<int> wire.
    // ------------------------------------------------------------------------
    var paramAuth = scope.ServiceProvider
        .GetRequiredService<AuthorizerFor<MinAgeRequest>>();
    var paramResult = await paramAuth.EvaluateAsync(new AotAgedContext("alice", 21));
    if (!paramResult.IsSuccess)
        throw new InvalidOperationException("[Parameterized] AOT smoke: expected success");

    Console.WriteLine("AOT [RequirePolicy(MinAge,18)] OK");

    // ------------------------------------------------------------------------
    // v2.1 - IResourceSecurityContext<T> probe. Verifies the interface
    // surfaces in AOT (the contract is shipped in v2.1; host packages adopt
    // by populating it later — until then a downcast falls through to false).
    // ------------------------------------------------------------------------
    IResourceSecurityContext<string>? probe = null;
    Console.WriteLine($"[IResourceSecurityContext] probe is {(probe is null ? "null" : "set")}");
}

sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx.Roles.Contains("Admin")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode));
}

sealed record TestContext(string Id,
                          IReadOnlySet<string> Roles,
                          IReadOnlyDictionary<string, string> Claims) : ISecurityContext;

[Policy("aot")]
internal sealed class AotPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(UnitResult<AuthorizationFailure>.Success());
}

[RequirePolicy("aot")]
internal sealed record AotSmokeRequest;

// ----------------------------------------------------------------------------
// v2.1 fixtures — kept top-level (no nested types) so the Discovery walker
// surfaces them under AOT publish (see Task 10).
// ----------------------------------------------------------------------------

[Policy("Premium")]
internal sealed class AotPremiumPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(UnitResult<AuthorizationFailure>.Success());
}

[Policy("Trusted")]
internal sealed class AotTrustedPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(new AuthorizationFailure("trusted.no"));
}

[RequireAnyPolicy("Premium", "Trusted")]
internal sealed record OrRequest;

[Policy("MinAge")]
internal sealed class AotMinAgePolicy : IAuthorizationPolicy<int>
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, int minAge, CancellationToken ct = default)
        => new(ctx is AotAgedContext aged && aged.Age >= minAge
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("age.below", $"age {(ctx as AotAgedContext)?.Age} < {minAge}"));
}

[RequirePolicy("MinAge", 18)]
internal sealed record MinAgeRequest;

internal sealed class AotAgedContext : ISecurityContext
{
    public AotAgedContext(string id, int age)
    {
        Id = id;
        Age = age;
        Roles = new HashSet<string>();
        Claims = new Dictionary<string, string>();
    }
    public string Id { get; }
    public int Age { get; }
    public IReadOnlySet<string> Roles { get; }
    public IReadOnlyDictionary<string, string> Claims { get; }
}
