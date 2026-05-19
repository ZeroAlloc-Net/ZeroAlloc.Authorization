using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.AotSmoke.Internal;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Results;

var ctx = new TestContext("alice",
    new HashSet<string> { "Admin" },
    new Dictionary<string, string>());
var policy = new AdminOnlyPolicy();

if (!policy.IsAuthorized(ctx)) throw new("IsAuthorized regressed");
if (!((IAuthorizationPolicy)policy).Evaluate(ctx).IsSuccess) throw new("Evaluate regressed");
if (!await ((IAuthorizationPolicy)policy).IsAuthorizedAsync(ctx)) throw new("IsAuthorizedAsync regressed");
if (!(await ((IAuthorizationPolicy)policy).EvaluateAsync(ctx)).IsSuccess) throw new("EvaluateAsync regressed");

if (policy.IsAuthorized(AnonymousSecurityContext.Instance))
    throw new("Anonymous should be denied");

Console.WriteLine("AOT behavior OK");

// Allocation budget gate — run AFTER behavior assertions so behavior failures are reported first.
// Asserts the README's "Zero allocation on all four hot-path methods" claim under the
// AOT runtime (catches trim/escape-analysis regressions the JIT-side test misses).
AllocationGate.AssertBudget(0, 1000, () => _ = policy.IsAuthorized(ctx), "IsAuthorized (allow)");
AllocationGate.AssertBudget(0, 1000,
    () => _ = policy.IsAuthorized(AnonymousSecurityContext.Instance),
    "IsAuthorized (deny anonymous)");
AllocationGate.AssertBudget(0, 1000, () => _ = ((IAuthorizationPolicy)policy).Evaluate(ctx), "Evaluate (allow)");
AllocationGate.AssertBudgetValueTask(0, 1000, () => ((IAuthorizationPolicy)policy).IsAuthorizedAsync(ctx), "IsAuthorizedAsync (allow)");
AllocationGate.AssertBudgetValueTask(0, 1000, () => ((IAuthorizationPolicy)policy).EvaluateAsync(ctx), "EvaluateAsync (allow)");

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
}

[AuthorizationPolicy("AdminOnly")]
sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
}

sealed record TestContext(string Id,
                          IReadOnlySet<string> Roles,
                          IReadOnlyDictionary<string, string> Claims) : ISecurityContext;

[Policy("aot")]
internal sealed class AotPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => true;
}

[RequirePolicy("aot")]
internal sealed record AotSmokeRequest;
