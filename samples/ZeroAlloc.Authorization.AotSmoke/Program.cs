using ZeroAlloc.Authorization;
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

Console.WriteLine("AOT smoke OK");
return 0;

[AuthorizationPolicy("AdminOnly")]
sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
}

sealed record TestContext(string Id,
                          IReadOnlySet<string> Roles,
                          IReadOnlyDictionary<string, string> Claims) : ISecurityContext;
