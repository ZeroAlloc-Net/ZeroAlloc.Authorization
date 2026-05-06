using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.AotSmoke.Internal;
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

[AuthorizationPolicy("AdminOnly")]
sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
}

sealed record TestContext(string Id,
                          IReadOnlySet<string> Roles,
                          IReadOnlyDictionary<string, string> Claims) : ISecurityContext;
