using BenchmarkDotNet.Attributes;
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class PolicyEvaluationBenchmarks
{
    private readonly IAuthorizationPolicy _policy = new AdminOnlyPolicy();
    private readonly TestContext _ctx = new("alice",
        new HashSet<string> { "Admin" },
        new Dictionary<string, string>());

    [Benchmark]
    public bool IsAuthorized() => _policy.IsAuthorized(_ctx);

    [Benchmark]
    public async ValueTask<bool> IsAuthorizedAsync()
        => await _policy.IsAuthorizedAsync(_ctx);

    [Benchmark]
    public UnitResult<AuthorizationFailure> Evaluate() => _policy.Evaluate(_ctx);

    [Benchmark]
    public async ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync()
        => await _policy.EvaluateAsync(_ctx);

    private sealed class AdminOnlyPolicy : IAuthorizationPolicy
    {
        public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
    }

    private sealed record TestContext(string Id,
                                      IReadOnlySet<string> Roles,
                                      IReadOnlyDictionary<string, string> Claims) : ISecurityContext;
}
